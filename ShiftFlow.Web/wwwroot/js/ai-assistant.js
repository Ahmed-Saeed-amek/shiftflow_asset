/* STEP AI assistant — drives BOTH the full page (Views/AiAssistant/Index) and the slide-over
   drawer (_AiDrawer), which share the same markup (_AiChat) and therefore the same element ids;
   only one of the two is ever on a page at a time.

   Every server-provided value (URLs, localized strings, voice defaults) arrives via
   window.aiAssistantConfig, emitted by _Layout — no Razor lives in this file.

   The conversation is kept in sessionStorage so it survives navigating between pages with the
   drawer open, and the whole log is re-rendered from that state on every change — which is what
   makes a confirm card's confirmed/cancelled state survive a re-render and a reload. */
(function () {
    'use strict';

    const cfg = window.aiAssistantConfig || {};
    const urls = cfg.urls || {};
    const S = cfg.strings || {};
    const speech = cfg.speech || {};

    const STORE_KEY = 'step.ai.conversation';
    const DRAWER_KEY = 'step.ai.drawer.open';
    const MAX_TURNS = 40;

    /** [{ role:'user'|'assistant'|'error', text, attachments?, ts, retryOf?, unauthorized? }] */
    let turns = loadTurns();
    const pageContext = readPageContext();
    let contextEnabled = true;

    let speechConfig = null;
    let recognizer = null;
    let isListening = false;
    let isBusy = false;
    let voiceRequested = false;

    // Real-time Avatar (video) state — full page only; the drawer has no avatar scene.
    let avatarSynth = null;
    let avatarPc = null;
    let avatarReady = false;

    const $ = id => document.getElementById(id);
    const el = {};

    document.addEventListener('DOMContentLoaded', init);

    function init() {
        el.messages = $('aiMessages');
        if (!el.messages) return; // no chat on this page (layout-less view, error page, …)

        el.typing = $('aiTyping');
        el.input = $('aiInput');
        el.send = $('btnSend');
        el.mic = $('btnMic');
        el.micIcon = $('micIcon');
        el.stopSpeak = $('btnStopSpeak');
        el.clear = $('btnClear');
        el.chips = $('aiChips');
        el.contextPill = $('aiContextPill');
        el.contextText = $('aiContextText');
        el.contextDismiss = $('aiContextDismiss');
        // Avatar panel: full page only, so every use of these is guarded.
        el.scene = $('avatarScene');
        el.overlay = $('avatarOverlay');
        el.overlayText = $('avatarOverlayText');
        el.overlaySpinner = $('avatarOverlaySpinner');
        el.enableVoice = $('btnEnableVoice');

        el.send.addEventListener('click', sendMessage);
        el.mic.addEventListener('click', toggleMic);
        el.stopSpeak.addEventListener('click', stopCurrentSpeech);
        if (el.clear) el.clear.addEventListener('click', clearChat);
        if (el.enableVoice) el.enableVoice.addEventListener('click', enableVoice);
        if (el.contextDismiss) el.contextDismiss.addEventListener('click', dismissContext);

        // Enter sends, Shift+Enter inserts a newline (the input is a textarea).
        el.input.addEventListener('keydown', e => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendMessage();
            }
        });
        el.input.addEventListener('input', autoGrow);

        renderContextPill();
        render();
        initDrawer();

        // A tab close/refresh never runs cleanup below, so without this the avatar session
        // lingers server-side until Azure's own idle timeout reclaims it.
        window.addEventListener('pagehide', () => {
            try { avatarSynth?.close(); } catch { /* already closing */ }
            try { avatarPc?.close(); } catch { /* already closing */ }
        });

        if (el.overlay) showOverlay(S.voiceOff, { spinner: false, showEnable: true });
    }

    /* ---------------------------------------------------------------- session state */

    function loadTurns() {
        try {
            const raw = sessionStorage.getItem(STORE_KEY);
            const parsed = raw ? JSON.parse(raw) : null;
            return Array.isArray(parsed) ? parsed.slice(-MAX_TURNS) : [];
        } catch {
            return []; // private mode / cleared storage / corrupt value — start fresh
        }
    }

    function saveTurns() {
        if (turns.length > MAX_TURNS) turns = turns.slice(-MAX_TURNS);
        try { sessionStorage.setItem(STORE_KEY, JSON.stringify(turns)); } catch { /* quota/private mode */ }
    }

    /** Assistant turns contribute only their answerText — attachments never go back up. */
    function history() {
        return turns
            .filter(t => t.role === 'user' || t.role === 'assistant')
            .map(t => ({ role: t.role, text: t.text }));
    }

    /* ---------------------------------------------------------------- page context */

    function readPageContext() {
        const meta = document.querySelector('meta[name="ai-context"]');
        if (!meta) return null;
        try {
            const parsed = JSON.parse(meta.getAttribute('content') || 'null');
            return parsed && typeof parsed === 'object' ? parsed : null;
        } catch {
            return null;
        }
    }

    /** What the pill shows: the most specific label we have for "what you're looking at". */
    function contextLabel() {
        if (!pageContext) return '';
        return pageContext.title || pageContext.entityType || pageContext.page || '';
    }

    function renderContextPill() {
        if (!el.contextPill) return;
        const label = contextLabel();
        const show = contextEnabled && !!label;
        el.contextPill.classList.toggle('d-none', !show);
        if (show) el.contextText.textContent = (S.viewing || 'Viewing') + ' ' + label;
    }

    function dismissContext() {
        contextEnabled = false;
        renderContextPill();
        renderChips();
        el.input.focus();
    }

    /* ---------------------------------------------------------------- quick chips */

    function chipSet() {
        const chips = S.chips || {};
        const type = contextEnabled && pageContext ? pageContext.entityType : null;
        if (type === 'WorkOrder') return chips.workOrder || [];
        if (type === 'Asset') return chips.asset || [];
        return chips.fallback || [];
    }

    function renderChips() {
        if (!el.chips) return;
        el.chips.textContent = '';
        chipSet().forEach(c => {
            const b = document.createElement('button');
            b.type = 'button';
            b.className = 'ai-chip';
            b.textContent = c.label;
            b.title = c.prompt;
            b.addEventListener('click', () => sendQuick(c.prompt));
            el.chips.appendChild(b);
        });
    }

    /* ---------------------------------------------------------------- fetch helpers */

    function antiForgeryToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    }

    /** Thrown for any non-2xx/unusable response; .kind lets callers show a specific message. */
    class HttpError extends Error {
        constructor(kind, message) {
            super(message);
            this.kind = kind;
        }
    }

    function classifyResponse(resp) {
        // A cookie-auth challenge answers an XHR with a 302 to the login page, which fetch
        // follows transparently and hands back as a 200 HTML document — so an unexpected
        // content type means "signed out", not "malformed JSON".
        const isJson = (resp.headers.get('content-type') || '').includes('json');
        if (resp.status === 401 || resp.status === 403 || (resp.ok && !isJson)) {
            return new HttpError('unauthorized', S.sessionExpired);
        }
        if (resp.status === 429) return new HttpError('rateLimited', S.rateLimited);
        if (!resp.ok) return new HttpError('http', 'HTTP ' + resp.status);
        return null;
    }

    /** fetch + status classification + JSON parse. Throws HttpError on anything unusable. */
    async function fetchJson(url, options) {
        let resp;
        try {
            resp = await fetch(url, options);
        } catch {
            throw new HttpError('network', S.networkError);
        }
        const err = classifyResponse(resp);
        if (err) {
            // A 4xx from Query still carries a friendly {error} body — prefer it.
            if (err.kind === 'http') {
                const body = await resp.json().catch(() => null);
                if (body && body.error) throw new HttpError('http', body.error);
            }
            throw err;
        }
        try {
            return await resp.json();
        } catch {
            throw new HttpError('network', S.networkError);
        }
    }

    function postJson(url, payload) {
        return fetchJson(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': antiForgeryToken(),
            },
            body: JSON.stringify(payload),
        });
    }

    /* ---------------------------------------------------------------- speech + avatar */

    // Both the plain-audio synthesizer and the WebRTC avatar need the same short-lived token;
    // fetch it at most once per page and share the promise between them.
    let speechTokenPromise = null;

    function getSpeechToken() {
        speechTokenPromise ??= fetchJson(urls.speechToken).catch(e => {
            speechTokenPromise = null; // allow a retry after a transient failure
            throw e;
        });
        return speechTokenPromise;
    }

    // The Speech SDK is ~1MB and the drawer now exists on every page of the app — so it is
    // pulled in only once the user actually opts into voice, instead of on every page load.
    let sdkPromise = null;

    function loadSpeechSdk() {
        if (typeof SpeechSDK !== 'undefined') return Promise.resolve(true);
        if (!urls.speechSdk) return Promise.resolve(false);
        sdkPromise ??= new Promise(resolve => {
            const s = document.createElement('script');
            s.src = urls.speechSdk;
            s.crossOrigin = 'anonymous';
            s.onload = () => resolve(typeof SpeechSDK !== 'undefined');
            s.onerror = () => { sdkPromise = null; resolve(false); };
            document.head.appendChild(s);
        });
        return sdkPromise;
    }

    /** User-initiated: brings up voice (STT + TTS) and, if available, the video avatar. */
    async function enableVoice() {
        if (voiceRequested) return;
        voiceRequested = true;
        if (el.enableVoice) el.enableVoice.classList.add('d-none');
        showOverlay(S.connectingAvatar, { spinner: true, showEnable: false });

        const speechOk = await initSpeech();
        if (!speechOk) {
            voiceRequested = false;
            showOverlay(S.voiceUnavailable, { spinner: false, showEnable: true, error: true });
            return;
        }
        // The video avatar is a full-page-only affordance; in the drawer voice stays audio-only.
        if (el.scene) await initAvatar();
        else hideOverlay();
    }

    async function initSpeech() {
        if (speechConfig) return true;
        if (!await loadSpeechSdk()) return false;
        try {
            const d = await getSpeechToken();
            speechConfig = SpeechSDK.SpeechConfig.fromAuthorizationToken(d.token, d.region);
            speechConfig.speechRecognitionLanguage = speech.locale;
            speechConfig.speechSynthesisVoiceName = speech.voice;
            // Azure's default end-of-speech silence detection is ~500ms-1s, which cut the
            // mic off after any brief thinking pause — stretch it to 2s. Both properties
            // control the same behavior across SDK versions/paths, so set both.
            speechConfig.setProperty(SpeechSDK.PropertyId.Speech_SegmentationSilenceTimeoutMs, '2000');
            speechConfig.setProperty(SpeechSDK.PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, '2000');
            return true;
        } catch (e) {
            console.warn('AI Assistant: speech unavailable —', e);
            return false;
        }
    }

    function setAvatarState(state) {
        if (el.scene) {
            el.scene.classList.remove('state-listening', 'state-thinking', 'state-speaking');
            if (state) el.scene.classList.add('state-' + state);
        }
        // The stop button only makes sense while the assistant is actually talking — both
        // speakText (avatar/audio-only) and stopCurrentSpeech route through this function.
        el.stopSpeak.classList.toggle('d-none', state !== 'speaking');
    }

    function showOverlay(text, opts) {
        if (!el.overlay) return;
        const o = opts || {};
        el.overlayText.textContent = text || '';
        el.overlaySpinner.classList.toggle('d-none', !o.spinner);
        el.enableVoice.classList.toggle('d-none', !o.showEnable);
        el.overlay.classList.toggle('error', !!o.error);
        el.overlay.classList.remove('hidden');
    }

    function hideOverlay() {
        if (el.overlay) el.overlay.classList.add('hidden');
    }

    async function initAvatar() {
        // startAvatarAsync negotiates a WebRTC session with Azure's avatar service and has no
        // built-in timeout of its own — race it against a fixed one so a stuck connection
        // falls back to the same "unavailable" message a fast failure would show.
        const timeout = ms => new Promise((_, reject) =>
            setTimeout(() => reject(new Error('avatar connection timed out')), ms));
        try {
            await Promise.race([initAvatarInner(), timeout(8000)]);
        } catch (e) {
            console.warn('AI Assistant: avatar unavailable —', e);
            showOverlay(S.avatarUnavailable, { spinner: false, showEnable: false, error: true });
        }
    }

    async function initAvatarInner() {
        const [speechResp, iceResp] = await Promise.all([
            getSpeechToken(),
            fetchJson(urls.avatarIceToken),
        ]);

        const avatarSpeechConfig = SpeechSDK.SpeechConfig.fromAuthorizationToken(speechResp.token, speechResp.region);
        avatarSpeechConfig.speechSynthesisVoiceName = speech.avatarVoice;

        const videoFormat = new SpeechSDK.AvatarVideoFormat();
        const avatarConfig = new SpeechSDK.AvatarConfig(speech.avatarCharacter, speech.avatarStyle, videoFormat);
        avatarConfig.backgroundColor = speech.avatarBackground;

        const videoStream = new MediaStream();
        const audioStream = new MediaStream();
        avatarPc = new RTCPeerConnection({
            iceServers: [{ urls: iceResp.Urls, username: iceResp.Username, credential: iceResp.Password }],
        });
        avatarPc.ontrack = e => {
            if (e.track.kind === 'video') {
                videoStream.addTrack(e.track);
                $('avatarVideo').srcObject = videoStream;
            } else {
                audioStream.addTrack(e.track);
                $('avatarAudio').srcObject = audioStream;
            }
        };
        avatarPc.addTransceiver('video', { direction: 'sendrecv' });
        avatarPc.addTransceiver('audio', { direction: 'sendrecv' });

        avatarSynth = new SpeechSDK.AvatarSynthesizer(avatarSpeechConfig, avatarConfig);
        const result = await avatarSynth.startAvatarAsync(avatarPc);

        if (result.reason === SpeechSDK.ResultReason.Canceled) {
            const details = SpeechSDK.CancellationDetails.fromResult(result);
            throw new Error(details.errorDetails || 'Avatar failed to start');
        }
        avatarReady = true;
        hideOverlay();
    }

    /* ---------------------------------------------------------------- microphone */

    async function toggleMic() {
        if (isListening) { stopListening(); return; }
        // First mic press doubles as the voice opt-in, so the token isn't fetched on load.
        if (!speechConfig) {
            el.mic.disabled = true;
            try { await enableVoice(); } finally { el.mic.disabled = false; }
            if (!speechConfig) { pushTurn({ role: 'error', text: S.voiceUnavailable }); return; }
        }
        startListening();
    }

    function startListening() {
        // Talking over the assistant interrupts it right away.
        stopCurrentSpeech();

        isListening = true;
        el.mic.classList.add('listening');
        el.mic.setAttribute('aria-pressed', 'true');
        el.micIcon.className = 'bi bi-mic-fill';
        setAvatarState('listening');

        const audioConfig = SpeechSDK.AudioConfig.fromDefaultMicrophoneInput();
        recognizer = new SpeechSDK.SpeechRecognizer(speechConfig, audioConfig);

        // Live interim preview while still speaking, so there's feedback during the
        // (now longer) pause before Azure decides the utterance is finished.
        recognizer.recognizing = (s, e) => {
            if (e.result.text) el.input.value = e.result.text;
        };

        recognizer.recognizeOnceAsync(result => {
            stopListening();
            if (result.reason === SpeechSDK.ResultReason.RecognizedSpeech && result.text) {
                el.input.value = result.text;
                sendMessage();
            }
        }, () => stopListening());
    }

    function stopListening() {
        isListening = false;
        el.mic.classList.remove('listening');
        el.mic.setAttribute('aria-pressed', 'false');
        el.micIcon.className = 'bi bi-mic';
        setAvatarState(null);
        try { recognizer?.close(); } catch { /* already closed */ }
        recognizer = null;
    }

    /* ---------------------------------------------------------------- speaking */

    // Tracks whichever audio-only synthesizer is currently speaking, if any, so a new
    // message can interrupt it (the real-time Avatar interrupts via stopSpeakingAsync).
    let currentAudioSynth = null;

    function stopCurrentSpeech() {
        if (avatarReady && avatarSynth) {
            avatarSynth.stopSpeakingAsync().catch(() => {});
        }
        if (currentAudioSynth) {
            const s = currentAudioSynth;
            currentAudioSynth = null;
            try { s.close(); } catch { /* already closing/closed */ }
        }
        setAvatarState(null);
    }

    async function speakText(text, voice) {
        if (!speechConfig && !avatarReady) return;
        setAvatarState('speaking');

        // Prefer the real-time video Avatar when its WebRTC session is up — it carries its own
        // audio track (routed to #avatarAudio via ontrack), so no separate audio-only synth runs
        // alongside it. Falls back to plain audio-only TTS if the avatar never came up.
        if (avatarReady && avatarSynth) {
            try {
                const result = await avatarSynth.speakTextAsync(text);
                if (result.reason === SpeechSDK.ResultReason.Canceled) {
                    const details = SpeechSDK.CancellationDetails.fromResult(result);
                    throw new Error(details.errorDetails || 'Avatar speech failed');
                }
            } catch (e) {
                console.warn('AI Assistant: avatar speech failed, falling back to audio-only —', e);
                speakTextAudioOnly(text, voice);
            } finally {
                setAvatarState(null);
            }
            return;
        }

        speakTextAudioOnly(text, voice);
    }

    function speakTextAudioOnly(text, voice) {
        if (!speechConfig) { setAvatarState(null); return; }
        if (voice) speechConfig.speechSynthesisVoiceName = voice;
        const audioConfig = SpeechSDK.AudioConfig.fromDefaultSpeakerOutput();
        const synth = new SpeechSDK.SpeechSynthesizer(speechConfig, audioConfig);
        currentAudioSynth = synth;
        const stopSpeaking = () => {
            setAvatarState(null);
            if (currentAudioSynth === synth) currentAudioSynth = null;
            synth.close();
        };
        synth.speakTextAsync(text, stopSpeaking, stopSpeaking);
    }

    /* ---------------------------------------------------------------- conversation */

    function autoGrow() {
        el.input.style.height = 'auto';
        el.input.style.height = Math.min(el.input.scrollHeight, 120) + 'px';
    }

    function sendMessage() {
        const text = el.input.value.trim();
        if (!text || isBusy) return;
        el.input.value = '';
        autoGrow();
        doSend(text);
    }

    function sendQuick(text) {
        if (!text || isBusy) return;
        doSend(text);
    }

    function pushTurn(turn) {
        turn.ts = turn.ts || Date.now();
        turns.push(turn);
        saveTurns();
        render();
    }

    async function doSend(text) {
        // A new message always wins — cut off whatever the assistant is still saying.
        stopCurrentSpeech();

        isBusy = true;
        el.send.disabled = true;

        const priorHistory = history();
        pushTurn({ role: 'user', text });
        showTyping(true);
        setAvatarState('thinking');

        try {
            const payload = { text, history: priorHistory };
            if (contextEnabled && pageContext) payload.context = pageContext;
            const data = await postJson(urls.query, payload);

            const answer = data.answerText || S.noResponse;
            showTyping(false);
            pushTurn({ role: 'assistant', text: answer, attachments: normalizeAttachments(data.attachments) });
            // Voice reads the answer text only — never attachment content.
            // Fire-and-forget: speech playback must not hold the send button disabled.
            if (data.voice) speakText(stripForSpeech(answer), data.voice);
            else setAvatarState(null);
        } catch (e) {
            showTyping(false);
            setAvatarState(null);
            // The failed turn is dropped from history so the next send isn't paired with a user
            // message the server never answered — it comes back via the Retry button instead.
            turns.pop();
            const isHttp = e instanceof HttpError;
            pushTurn({
                role: 'error',
                text: isHttp ? e.message : S.genericError,
                retryOf: text,
                unauthorized: isHttp && e.kind === 'unauthorized',
            });
        } finally {
            isBusy = false;
            el.send.disabled = false;
            el.input.focus();
            scrollBottom();
        }
    }

    /** Tolerates a backend that doesn't send attachments yet, or sends something unexpected. */
    function normalizeAttachments(list) {
        return Array.isArray(list) ? list.filter(a => a && typeof a.kind === 'string') : [];
    }

    function clearChat() {
        turns = [];
        saveTurns();
        render();
        el.input.focus();
    }

    function showTyping(show) {
        el.typing.style.display = show ? 'flex' : 'none';
        if (show) scrollBottom();
    }

    function scrollBottom() {
        el.messages.scrollTop = el.messages.scrollHeight;
    }

    /* ---------------------------------------------------------------- rendering */

    function render() {
        [...el.messages.children].forEach(c => { if (c !== el.typing) c.remove(); });

        if (!turns.length) {
            el.messages.insertBefore(bubble('assistant', S.greeting || '', []), el.typing);
        } else {
            turns.forEach(t => el.messages.insertBefore(renderTurn(t), el.typing));
        }
        renderChips();
        scrollBottom();
    }

    function renderTurn(turn) {
        if (turn.role === 'error') return errorBubble(turn);
        return bubble(turn.role, turn.text, turn.attachments || [], turn);
    }

    /** Shown on hover rather than inline — a timestamp per bubble is noise in a short chat. */
    function timestampTitle(turn) {
        if (!turn || !turn.ts) return '';
        try {
            return new Date(turn.ts).toLocaleString(document.documentElement.lang || undefined);
        } catch {
            return '';
        }
    }

    function bubble(role, text, attachments, turn) {
        const div = document.createElement('div');
        div.className = 'ai-bubble ' + role;
        const ts = timestampTitle(turn);
        if (ts) div.title = ts;

        const body = document.createElement('div');
        body.className = 'ai-bubble-text';
        if (role === 'assistant') body.innerHTML = formatAiText(text);
        else body.textContent = text;
        div.appendChild(body);

        (attachments || []).forEach(att => {
            const node = renderAttachment(att);
            if (node) div.appendChild(node);
        });

        if (role === 'assistant') div.appendChild(copyButton(text));
        return div;
    }

    function copyButton(text) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'ai-copy';
        btn.title = S.copy || 'Copy';
        btn.setAttribute('aria-label', S.copy || 'Copy');
        btn.innerHTML = '<i class="bi bi-clipboard" aria-hidden="true"></i>';
        btn.addEventListener('click', async () => {
            try {
                await navigator.clipboard.writeText(text);
                btn.innerHTML = '<i class="bi bi-check2" aria-hidden="true"></i>';
                btn.title = S.copied || 'Copied';
                setTimeout(() => {
                    btn.innerHTML = '<i class="bi bi-clipboard" aria-hidden="true"></i>';
                    btn.title = S.copy || 'Copy';
                }, 1500);
            } catch { /* clipboard blocked (insecure context / permission) — nothing useful to say */ }
        });
        return btn;
    }

    function errorBubble(turn) {
        const div = document.createElement('div');
        div.className = 'ai-bubble error';
        const ts = timestampTitle(turn);
        if (ts) div.title = ts;

        const p = document.createElement('div');
        p.className = 'ai-bubble-text';
        p.textContent = turn.text;
        div.appendChild(p);

        const actions = document.createElement('div');
        actions.className = 'ai-att-actions';
        if (turn.unauthorized) {
            const a = document.createElement('a');
            a.href = urls.login || '#';
            a.className = 'btn btn-sm btn-outline-primary';
            a.textContent = S.signIn || 'Sign in';
            actions.appendChild(a);
        } else if (turn.retryOf) {
            const b = document.createElement('button');
            b.type = 'button';
            b.className = 'btn btn-sm btn-outline-primary';
            b.innerHTML = '<i class="bi bi-arrow-clockwise me-1" aria-hidden="true"></i>';
            b.appendChild(document.createTextNode(S.retry || 'Retry'));
            b.addEventListener('click', () => {
                const idx = turns.indexOf(turn);
                if (idx >= 0) turns.splice(idx, 1);
                saveTurns();
                render();
                doSend(turn.retryOf);
            });
            actions.appendChild(b);
        }
        if (actions.childNodes.length) div.appendChild(actions);
        return div;
    }

    /* ---------------------------------------------------------------- attachments */

    function renderAttachment(att) {
        switch (att.kind) {
            case 'table': return renderTable(att);
            case 'cards': return renderCards(att);
            case 'links': return renderLinks(att);
            case 'download': return renderDownload(att);
            case 'confirm': return renderConfirm(att);
            default: return null; // an attachment kind this build doesn't know about
        }
    }

    function attWrap(extraClass, title) {
        const wrap = document.createElement('div');
        wrap.className = 'ai-att ' + extraClass;
        if (title) {
            const h = document.createElement('div');
            h.className = 'ai-att-title';
            h.textContent = title;
            wrap.appendChild(h);
        }
        return wrap;
    }

    function renderTable(att) {
        const cols = Array.isArray(att.columns) ? att.columns : [];
        const rows = Array.isArray(att.rows) ? att.rows : [];
        if (!cols.length || !rows.length) return null;

        const wrap = attWrap('ai-att-table', att.title);
        const scroller = document.createElement('div');
        scroller.className = 'table-responsive';
        const table = document.createElement('table');
        table.className = 'table table-sm align-middle mb-0';

        const thead = document.createElement('thead');
        const htr = document.createElement('tr');
        cols.forEach(c => {
            const th = document.createElement('th');
            th.scope = 'col';
            th.textContent = c.label ?? c.key ?? '';
            htr.appendChild(th);
        });
        thead.appendChild(htr);
        table.appendChild(thead);

        const tbody = document.createElement('tbody');
        rows.forEach(r => {
            const tr = document.createElement('tr');
            if (r._url) {
                // Same convention every list page uses — site.js's delegated row-link handler
                // picks these up, including rows added to the DOM after load.
                tr.className = 'row-link cursor-pointer';
                tr.setAttribute('data-href', r._url);
                tr.setAttribute('tabindex', '0');
            }
            let statusDone = false;
            cols.forEach((c, i) => {
                const td = document.createElement('td');
                const value = r[c.key];
                const isStatusCell = !statusDone && r._status != null &&
                    (String(value) === String(r._status) ||
                        /status|state|priority|stage|condition/i.test(c.key || ''));
                if (isStatusCell) {
                    statusDone = true;
                    td.appendChild(statusBadge(value == null ? r._status : value));
                } else if (i === 0 && r._url) {
                    const a = document.createElement('a');
                    a.href = r._url;
                    a.textContent = value == null ? '' : String(value);
                    td.appendChild(a);
                } else {
                    td.textContent = value == null ? '' : String(value);
                }
                tr.appendChild(td);
            });
            tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        scroller.appendChild(table);
        wrap.appendChild(scroller);
        return wrap;
    }

    function renderCards(att) {
        const items = Array.isArray(att.items) ? att.items : [];
        if (!items.length) return null;
        const wrap = attWrap('ai-att-cards', att.title);
        const grid = document.createElement('div');
        grid.className = 'ai-card-grid';

        items.forEach(it => {
            const card = document.createElement(it.url ? 'a' : 'div');
            card.className = 'ai-card';
            if (it.url) card.href = it.url;

            const head = document.createElement('div');
            head.className = 'ai-card-head';
            const t = document.createElement('span');
            t.className = 'ai-card-title';
            t.textContent = it.title ?? '';
            head.appendChild(t);
            if (it.badge && it.badge.text) {
                head.appendChild(statusBadge(it.badge.status || it.badge.text, it.badge.text));
            }
            card.appendChild(head);

            if (it.subtitle) {
                const sub = document.createElement('div');
                sub.className = 'ai-card-sub';
                sub.textContent = it.subtitle;
                card.appendChild(sub);
            }

            (Array.isArray(it.fields) ? it.fields : []).forEach(f => {
                const row = document.createElement('div');
                row.className = 'ai-card-field';
                const lab = document.createElement('span');
                lab.className = 'ai-card-label';
                lab.textContent = f.label ?? '';
                const val = document.createElement('span');
                val.className = 'ai-card-value';
                val.textContent = f.value == null ? '' : String(f.value);
                row.append(lab, val);
                card.appendChild(row);
            });

            grid.appendChild(card);
        });

        wrap.appendChild(grid);
        return wrap;
    }

    function renderLinks(att) {
        const items = Array.isArray(att.items) ? att.items : [];
        if (!items.length) return null;
        const wrap = attWrap('ai-att-links', att.title);
        const row = document.createElement('div');
        row.className = 'ai-att-actions';
        items.forEach(it => {
            if (!it || !it.url) return;
            const a = document.createElement('a');
            a.className = 'btn btn-sm btn-outline-primary';
            a.href = it.url;
            if (it.icon) {
                const i = document.createElement('i');
                // Only ever a Bootstrap Icons name — sanitized, never interpolated as markup.
                i.className = 'bi bi-' + String(it.icon).replace(/[^a-z0-9-]/gi, '') + ' me-1';
                i.setAttribute('aria-hidden', 'true');
                a.appendChild(i);
            }
            a.appendChild(document.createTextNode(it.label ?? it.url));
            row.appendChild(a);
        });
        wrap.appendChild(row);
        return wrap;
    }

    function renderDownload(att) {
        if (!att.url) return null;
        const wrap = attWrap('ai-att-download');
        const row = document.createElement('div');
        row.className = 'ai-att-actions';
        const a = document.createElement('a');
        a.className = 'btn btn-sm btn-outline-primary';
        a.href = att.url;
        a.target = '_blank';
        a.rel = 'noopener noreferrer';
        a.innerHTML = '<i class="bi bi-download me-1" aria-hidden="true"></i>';
        a.appendChild(document.createTextNode(att.label || S.download || 'Download'));
        row.appendChild(a);
        wrap.appendChild(row);
        return wrap;
    }

    function renderConfirm(att) {
        const wrap = document.createElement('div');
        wrap.className = 'ai-att ai-confirm' + (att.danger ? ' danger' : '') + (att._state ? ' settled' : '');

        const title = document.createElement('div');
        title.className = 'ai-confirm-title';
        title.textContent = att.title || '';
        wrap.appendChild(title);

        if (att.summary) {
            const sum = document.createElement('div');
            sum.className = 'ai-confirm-summary';
            sum.textContent = att.summary;
            wrap.appendChild(sum);
        }

        const items = Array.isArray(att.items) ? att.items : [];
        if (items.length) {
            const ul = document.createElement('ul');
            ul.className = 'ai-confirm-items';
            items.slice(0, 10).forEach(s => {
                const li = document.createElement('li');
                li.textContent = String(s);
                ul.appendChild(li);
            });
            if (items.length > 10) {
                const li = document.createElement('li');
                li.className = 'ai-confirm-more';
                li.textContent = S.andMore || '…and more';
                ul.appendChild(li);
            }
            wrap.appendChild(ul);
        }

        const actions = document.createElement('div');
        actions.className = 'ai-att-actions';

        // _state lives on the attachment, which is part of the persisted turn — so a confirmed
        // or cancelled card stays settled across re-renders, navigations and reloads.
        if (att._state) {
            const done = document.createElement('span');
            done.className = 'ai-confirm-state';
            done.textContent = att._state === 'cancelled' ? (S.cancelled || 'Cancelled') : (S.confirmed || 'Done');
            actions.appendChild(done);
        } else {
            const ok = document.createElement('button');
            ok.type = 'button';
            ok.className = 'btn btn-sm ' + (att.danger ? 'btn-danger' : 'btn-primary');
            ok.textContent = att.actionLabel || S.confirmAction || 'Confirm';

            const no = document.createElement('button');
            no.type = 'button';
            no.className = 'btn btn-sm btn-outline-secondary';
            no.textContent = S.cancel || 'Cancel';

            const settle = state => {
                att._state = state;
                ok.disabled = true;
                no.disabled = true;
                saveTurns();
            };

            ok.addEventListener('click', () => {
                settle('confirmed');
                runConfirm(att.token);
            });
            no.addEventListener('click', () => {
                settle('cancelled');
                if (urls.dismiss) postJson(urls.dismiss, { token: att.token }).catch(() => {});
                render();
            });
            actions.append(ok, no);
        }

        wrap.appendChild(actions);
        return wrap;
    }

    async function runConfirm(token) {
        isBusy = true;
        el.send.disabled = true;
        showTyping(true);
        try {
            const data = await postJson(urls.confirm, { token });
            showTyping(false);
            pushTurn({
                role: 'assistant',
                text: data.answerText || S.noResponse,
                attachments: normalizeAttachments(data.attachments),
            });
        } catch (e) {
            showTyping(false);
            const isHttp = e instanceof HttpError;
            pushTurn({
                role: 'error',
                text: isHttp ? e.message : S.genericError,
                unauthorized: isHttp && e.kind === 'unauthorized',
            });
        } finally {
            isBusy = false;
            el.send.disabled = false;
            render();
        }
    }

    /* ---------------------------------------------------------------- status badges */

    // Mirrors Services/StatusStyle.cs — the same subtle-badge tones every list page uses, so a
    // status inside an assistant answer is coloured exactly like the same status in a table.
    const STATUS_TONES = {
        'Active': 'success', 'Inactive': 'secondary', 'Planned': 'primary', 'Completed': 'success',
        'Done': 'success', 'Cancelled': 'secondary', 'Pending': 'warning', 'Approved': 'success',
        'Rejected': 'danger', 'New': 'primary', 'Assigned': 'primary', 'In Progress': 'info',
        'Open': 'danger', 'Dispatched': 'primary', 'Resolved': 'success', 'Closed': 'secondary',
        'Escalated': 'warning', 'Under Maintenance': 'warning', 'Faulty': 'danger',
        'Decommissioned': 'secondary', 'In Storage': 'info', 'In Delivery': 'primary',
        'Spare Part': 'secondary', 'In Stock': 'success', 'Low Stock': 'warning',
        'Out of Stock': 'danger', 'Discontinued': 'secondary', 'Low': 'secondary',
        'Medium': 'primary', 'High': 'warning', 'Critical': 'danger', 'Emergency': 'danger',
        'Urgent': 'warning', 'Normal': 'secondary', 'Draft': 'secondary', 'Submitted': 'primary',
        'Overdue': 'danger', 'Working': 'success', 'Defective': 'danger', 'Maintenance': 'warning',
        'Retired': 'secondary', 'Suspended': 'danger', 'Acknowledged': 'info', 'Expired': 'secondary',
        'Purchase': 'primary', 'Warranty': 'info', 'Service': 'success', 'Insurance': 'warning',
        'Preventive Maintenance': 'dark', 'Sent to Vendor': 'info', 'Pending Approval': 'warning',
        'Fixed - Pending Confirmation': 'warning', 'Blocked': 'danger', 'OK': 'success',
        'Quick Check': 'info', 'Inspection': 'primary',
    };

    const LABEL_OVERRIDES = {
        'FixedPendingConfirmation': 'Fixed - Pending Confirmation',
        'OutOfStock': 'Out of Stock',
        'OK': 'OK',
        'HR': 'HR',
    };

    /** StatusStyle.Label in JS: "PendingApproval" and "Pending Approval" normalize identically. */
    function statusKey(raw) {
        const s = String(raw ?? '').trim();
        if (!s) return '';
        if (LABEL_OVERRIDES[s]) return LABEL_OVERRIDES[s];
        if (s.includes(' ')) return s;
        let out = '';
        for (let i = 0; i < s.length; i++) {
            const c = s[i];
            const isUpper = c >= 'A' && c <= 'Z';
            const prevUpper = i > 0 && s[i - 1] >= 'A' && s[i - 1] <= 'Z';
            const nextLower = i + 1 < s.length && s[i + 1] >= 'a' && s[i + 1] <= 'z';
            // Split before a capital that starts a new word, keeping acronym runs together.
            if (i > 0 && isUpper && (!prevUpper || nextLower)) out += ' ';
            out += c;
        }
        return out;
    }

    /**
     * @param {*} status raw status, used for the colour and (by default) the label
     * @param {*} [label] explicit display text, when the caller has its own wording
     */
    function statusBadge(status, label) {
        const key = statusKey(status);
        const tone = STATUS_TONES[key] || 'secondary';
        const span = document.createElement('span');
        span.className = 'badge border bg-' + tone + '-subtle text-' + tone + '-emphasis border-' + tone + '-subtle';
        const translated = (window.i18n && window.i18n.status && window.i18n.status[key]) || key;
        span.textContent = label != null ? String(label) : translated;
        return span;
    }

    /* ---------------------------------------------------------------- text formatting */

    // Speech-only cleanup: [label](url) links render as an embed/clickable link visually —
    // read aloud, only the label makes sense. Also drops bare URLs and ** bold markers.
    function stripForSpeech(t) {
        return t
            .replace(/\[([^\]]+)\]\(\S+?\)/g, '$1')
            .replace(/https?:\/\/\S+/g, '')
            .replace(/\*\*(.+?)\*\*/g, '$1');
    }

    // Order numbers and asset tags in a reply are human codes, not database ids — link them to
    // the matching list page's search so the record is one click away.
    const CODE_RE = /\b(?:WO|INS|MO)-\d{4}-\d{3,}\b|\bAST-\d{4}\b/g;

    function codeUrl(code) {
        if (code.startsWith('AST-')) return (urls.searchAssets || '/Assets') + '?q=' + encodeURIComponent(code);
        if (code.startsWith('WO-')) return (urls.searchWorkOrders || '/WorkOrders') + '?search=' + encodeURIComponent(code);
        return (urls.searchOrders || '/Orders') + '?search=' + encodeURIComponent(code);
    }

    // The assistant's text ultimately comes from an LLM completion, so it is never trusted as
    // raw HTML: escape first, then apply a small set of well-understood patterns on top.
    const escapeHtml = s => String(s).replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    /** Escape first, then auto-link codes — a matched code can't contain HTML, so this is safe. */
    function escapeAndLink(s) {
        return escapeHtml(s).replace(CODE_RE, code =>
            '<a href="' + escapeHtml(codeUrl(code)) + '">' + code + '</a>');
    }

    function formatAiText(text) {
        // getAssetRepairGuidance always constructs video links in exactly this shape —
        // re-validated here rather than trusted from the model's text, since a video ID is
        // the only thing ever used to build an iframe src.
        const YT_RE = /^https:\/\/www\.youtube\.com\/watch\?v=([A-Za-z0-9_-]{11})$/;

        // Converts markdown [label](url) links in one line into safe HTML. Everything outside
        // a recognized link syntax is HTML-escaped, so no raw text reaches the DOM unescaped.
        function linkify(rawLine) {
            const re = /\[([^\]]+)\]\((\S+?)\)/g;
            let out = '';
            let last = 0;
            let m;
            while ((m = re.exec(rawLine))) {
                out += escapeAndLink(rawLine.slice(last, m.index));
                const label = m[1], url = m[2];
                const yt = YT_RE.exec(url);
                if (yt) {
                    out += '<div class="ai-video-embed"><iframe src="https://www.youtube-nocookie.com/embed/' + yt[1] + '" ' +
                        'title="' + escapeHtml(label) + '" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture" ' +
                        'allowfullscreen loading="lazy" referrerpolicy="strict-origin-when-cross-origin"></iframe></div>';
                } else if (/^https:\/\//.test(url)) {
                    out += '<a href="' + escapeHtml(url) + '" target="_blank" rel="noopener noreferrer">' + escapeHtml(label) + '</a>';
                } else {
                    out += escapeAndLink(m[0]);
                }
                last = re.lastIndex;
            }
            out += escapeAndLink(rawLine.slice(last));
            return out;
        }

        const lines = String(text ?? '').split('\n');
        let html = '';
        let listType = null; // 'ul' | 'ol' | null

        function closeList() {
            if (listType) { html += '</' + listType + '>'; listType = null; }
        }

        for (const rawLine of lines) {
            const line = linkify(rawLine).replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
            const bullet = /^\s*[-*•]\s+(.*)$/.exec(line);
            const numbered = /^\s*(\d+)[.)]\s+(.*)$/.exec(line);

            if (bullet) {
                if (listType !== 'ul') { closeList(); html += '<ul>'; listType = 'ul'; }
                html += '<li>' + bullet[1] + '</li>';
            } else if (numbered) {
                // Nested bullets between numbered items break the <ol> into separate elements
                // (each restarting at 1) — pin the original number via value=.
                if (listType !== 'ol') { closeList(); html += '<ol>'; listType = 'ol'; }
                html += '<li value="' + numbered[1] + '">' + numbered[2] + '</li>';
            } else {
                closeList();
                if (line.trim() === '') html += '<br>';
                else html += '<p>' + line + '</p>';
            }
        }
        closeList();
        return html;
    }

    /* ---------------------------------------------------------------- drawer */

    function initDrawer() {
        const drawer = $('aiDrawer');
        if (!drawer || typeof bootstrap === 'undefined') return;

        drawer.addEventListener('shown.bs.offcanvas', () => {
            // Hides the floating launcher, which would otherwise sit on top of the panel.
            document.body.classList.add('ai-drawer-open');
            try { sessionStorage.setItem(DRAWER_KEY, '1'); } catch { /* private mode */ }
            el.input.focus();
            scrollBottom();
        });
        // Escape-to-close and returning focus to the launcher are Bootstrap's own behaviour.
        drawer.addEventListener('hidden.bs.offcanvas', () => {
            document.body.classList.remove('ai-drawer-open');
            try { sessionStorage.setItem(DRAWER_KEY, '0'); } catch { /* private mode */ }
        });

        let wasOpen = false;
        try { wasOpen = sessionStorage.getItem(DRAWER_KEY) === '1'; } catch { /* private mode */ }
        if (wasOpen) bootstrap.Offcanvas.getOrCreateInstance(drawer).show();
    }
})();
