/* AI Assistant page script — extracted from Views/AiAssistant/Index.cshtml.
   All server-provided values (URLs, localized strings, voice defaults) arrive via
   window.aiAssistantConfig, so no Razor lives in this file. */
(function () {
    'use strict';

    const cfg = window.aiAssistantConfig || {};
    const urls = cfg.urls || {};
    const S = cfg.strings || {};
    const speech = cfg.speech || {};

    const messages = [];
    let speechConfig = null;
    let recognizer = null;
    let isListening = false;
    let isBusy = false;
    let voiceRequested = false;

    // Real-time Avatar (video) state — separate from the plain-audio speechConfig above.
    let avatarSynth = null;
    let avatarPc = null;
    let avatarReady = false;

    const $ = id => document.getElementById(id);
    const el = {};

    document.addEventListener('DOMContentLoaded', init);

    function init() {
        el.messages = $('aiMessages');
        el.typing = $('aiTyping');
        el.input = $('aiInput');
        el.send = $('btnSend');
        el.mic = $('btnMic');
        el.micIcon = $('micIcon');
        el.stopSpeak = $('btnStopSpeak');
        el.clear = $('btnClear');
        el.scene = $('avatarScene');
        el.overlay = $('avatarOverlay');
        el.overlayText = $('avatarOverlayText');
        el.overlaySpinner = $('avatarOverlaySpinner');
        el.enableVoice = $('btnEnableVoice');

        el.send.addEventListener('click', sendMessage);
        el.mic.addEventListener('click', toggleMic);
        el.stopSpeak.addEventListener('click', stopCurrentSpeech);
        el.clear.addEventListener('click', clearChat);
        el.enableVoice.addEventListener('click', enableVoice);

        // Enter sends, Shift+Enter inserts a newline (the input is a textarea).
        el.input.addEventListener('keydown', e => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendMessage();
            }
        });

        document.querySelectorAll('.ai-chip[data-prompt]').forEach(chip => {
            chip.addEventListener('click', () => sendQuick(chip.dataset.prompt));
        });

        // A tab close/refresh never runs cleanup below, so without this the avatar session
        // lingers server-side until Azure's own idle timeout reclaims it.
        window.addEventListener('pagehide', () => {
            try { avatarSynth?.close(); } catch { /* already closing */ }
            try { avatarPc?.close(); } catch { /* already closing */ }
        });

        showOverlay(S.voiceOff, { spinner: false, showEnable: true });
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

    /** User-initiated: brings up voice (STT + TTS) and, if available, the video avatar. */
    async function enableVoice() {
        if (voiceRequested) return;
        voiceRequested = true;
        el.enableVoice.classList.add('d-none');
        showOverlay(S.connectingAvatar, { spinner: true, showEnable: false });

        const speechOk = await initSpeech();
        if (!speechOk) {
            voiceRequested = false;
            showOverlay(S.voiceUnavailable, { spinner: false, showEnable: true, error: true });
            return;
        }
        await initAvatar();
    }

    async function initSpeech() {
        if (speechConfig) return true;
        if (typeof SpeechSDK === 'undefined') return false;
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
        el.scene.classList.remove('state-listening', 'state-thinking', 'state-speaking');
        if (state) el.scene.classList.add('state-' + state);
        // The stop button only makes sense while the assistant is actually talking — both
        // speakText (avatar/audio-only) and stopCurrentSpeech route through this function.
        el.stopSpeak.classList.toggle('d-none', state !== 'speaking');
    }

    function showOverlay(text, opts) {
        const o = opts || {};
        el.overlayText.textContent = text || '';
        el.overlaySpinner.classList.toggle('d-none', !o.spinner);
        el.enableVoice.classList.toggle('d-none', !o.showEnable);
        el.overlay.classList.toggle('error', !!o.error);
        el.overlay.classList.remove('hidden');
    }

    function hideOverlay() {
        el.overlay.classList.add('hidden');
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
            if (!speechConfig) { addBubble(S.voiceUnavailable, 'error'); return; }
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

    function sendMessage() {
        const text = el.input.value.trim();
        if (!text || isBusy) return;
        el.input.value = '';
        doSend(text);
    }

    function sendQuick(text) {
        if (!text) return;
        el.input.value = text;
        sendMessage();
    }

    async function doSend(text) {
        // A new message always wins — cut off whatever the assistant is still saying.
        stopCurrentSpeech();

        isBusy = true;
        el.send.disabled = true;

        addBubble(text, 'user');
        messages.push({ role: 'user', text });
        showTyping(true);
        setAvatarState('thinking');
        scrollBottom();

        try {
            const data = await fetchJson(urls.query, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': antiForgeryToken(),
                },
                body: JSON.stringify({ text, history: messages.slice(0, -1) }),
            });

            const answer = data.answerText || S.noResponse;
            showTyping(false);
            addBubble(answer, 'assistant');
            messages.push({ role: 'assistant', text: answer });
            // Fire-and-forget: speech playback must not hold the send button disabled.
            if (data.voice) speakText(stripForSpeech(answer), data.voice);
            else setAvatarState(null);
        } catch (e) {
            showTyping(false);
            setAvatarState(null);
            // The failed turn is dropped from history so the next send isn't paired with
            // a user message the server never answered.
            messages.pop();
            addBubble(e instanceof HttpError ? e.message : S.genericError, 'error');
            if (e instanceof HttpError && e.kind === 'unauthorized') showLoginLink();
        } finally {
            isBusy = false;
            el.send.disabled = false;
            el.input.focus();
            scrollBottom();
        }
    }

    function showLoginLink() {
        const div = document.createElement('div');
        div.className = 'ai-bubble error';
        const a = document.createElement('a');
        a.href = urls.login;
        a.textContent = S.signIn;
        div.appendChild(a);
        el.messages.insertBefore(div, el.typing);
    }

    // Speech-only cleanup: [label](url) links render as an embed/clickable link visually —
    // read aloud, only the label makes sense. Also drops bare URLs and ** bold markers.
    function stripForSpeech(t) {
        return t
            .replace(/\[([^\]]+)\]\(\S+?\)/g, '$1')
            .replace(/https?:\/\/\S+/g, '')
            .replace(/\*\*(.+?)\*\*/g, '$1');
    }

    // Minimal, safe Markdown-ish formatter for assistant replies: escapes HTML first
    // (the text ultimately comes from an LLM completion, so never trust it as raw HTML),
    // then applies a small set of well-understood patterns — bold, lists, links.
    function formatAiText(text) {
        const escapeHtml = s => s.replace(/[&<>"']/g, c =>
            ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

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
                out += escapeHtml(rawLine.slice(last, m.index));
                const label = m[1], url = m[2];
                const yt = YT_RE.exec(url);
                if (yt) {
                    out += `<div class="ai-video-embed"><iframe src="https://www.youtube-nocookie.com/embed/${yt[1]}" ` +
                        `title="${escapeHtml(label)}" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture" ` +
                        `allowfullscreen loading="lazy" referrerpolicy="strict-origin-when-cross-origin"></iframe></div>`;
                } else if (/^https:\/\//.test(url)) {
                    out += `<a href="${escapeHtml(url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(label)}</a>`;
                } else {
                    out += escapeHtml(m[0]);
                }
                last = re.lastIndex;
            }
            out += escapeHtml(rawLine.slice(last));
            return out;
        }

        const lines = text.split('\n');
        let html = '';
        let listType = null; // 'ul' | 'ol' | null

        function closeList() {
            if (listType) { html += `</${listType}>`; listType = null; }
        }

        for (const rawLine of lines) {
            const line = linkify(rawLine).replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
            const bullet = /^\s*[-*]\s+(.*)$/.exec(line);
            const numbered = /^\s*(\d+)[.)]\s+(.*)$/.exec(line);

            if (bullet) {
                if (listType !== 'ul') { closeList(); html += '<ul>'; listType = 'ul'; }
                html += `<li>${bullet[1]}</li>`;
            } else if (numbered) {
                // Nested bullets between numbered items break the <ol> into separate elements
                // (each restarting at 1) — pin the original number via value=.
                if (listType !== 'ol') { closeList(); html += '<ol>'; listType = 'ol'; }
                html += `<li value="${numbered[1]}">${numbered[2]}</li>`;
            } else {
                closeList();
                if (line.trim() === '') html += '<br>';
                else html += `<p>${line}</p>`;
            }
        }
        closeList();
        return html;
    }

    function addBubble(text, role) {
        const div = document.createElement('div');
        div.className = `ai-bubble ${role}`;
        if (role === 'assistant') div.innerHTML = formatAiText(text);
        else div.textContent = text;
        el.messages.insertBefore(div, el.typing);
    }

    function showTyping(show) {
        el.typing.style.display = show ? 'flex' : 'none';
        if (show) scrollBottom();
    }

    function scrollBottom() {
        el.messages.scrollTop = el.messages.scrollHeight;
    }

    function clearChat() {
        messages.length = 0;
        // Remove all bubbles, keeping the typing indicator in place.
        [...el.messages.children]
            .filter(c => c !== el.typing && c.classList.contains('ai-bubble'))
            .forEach(c => c.remove());
        const greeting = document.createElement('div');
        greeting.className = 'ai-bubble assistant';
        greeting.textContent = S.chatCleared;
        el.messages.insertBefore(greeting, el.typing);
        el.input.focus();
    }
})();
