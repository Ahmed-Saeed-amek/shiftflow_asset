# New translation strings — AI Assistant

Add these to `ShiftFlow.Web/Localization/Translations.cs` (branch `fix/ai-assistant` does not
edit that file directly, per the parallel-agent file ownership rules).

| English (key) | Arabic |
| --- | --- |
| `Sign in` | تسجيل الدخول |
| `Enable voice` | تفعيل الصوت |
| `Clear conversation` | مسح المحادثة |
| `Voice and avatar are off.` | الصوت والشخصية الافتراضية متوقفان. |
| `Voice is unavailable right now.` | الصوت غير متاح حاليًا. |
| `Your session expired. Please sign in again.` | انتهت جلستك. يرجى تسجيل الدخول مرة أخرى. |
| `Too many requests. Please wait a moment and try again.` | طلبات كثيرة جدًا. يرجى الانتظار قليلاً ثم المحاولة مرة أخرى. |
| `Conversation history is too large. Please clear the chat and try again.` | سجل المحادثة كبير جدًا. يرجى مسح المحادثة والمحاولة مرة أخرى. |

Already present in `Translations.cs` and reused unchanged: `AI Assistant`,
`Ask about inspection orders, groups, and more — by voice or text.`, `Conversation`, `Clear`,
`Voice input`, `Stop speaking`, `Send`, `Connecting avatar…`,
`Ask me anything about inspection orders...`, `No response received.`,
`An error occurred. Please try again.`,
`Network error. Please check your connection and try again.`,
`Chat cleared. How can I help you?`, `Avatar unavailable — voice replies still work.`,
`Text is required`, `Text exceeds maximum length of 500 characters`,
`An error occurred processing your request. Please try again.`

## Not routed through `Loc.T`

`AiAssistantOrchestrator` has no `ILanguageService`; it already picks its fallback text from the
`lang` argument with inline literals (the pre-existing "I was unable to complete the request"
message does the same). The new empty-completion fallback follows that pattern:

| English | Arabic |
| --- | --- |
| `I couldn't produce a reply. Please rephrase your question and try again.` | لم أتمكن من إنشاء رد. يرجى إعادة صياغة سؤالك والمحاولة مرة أخرى. |
