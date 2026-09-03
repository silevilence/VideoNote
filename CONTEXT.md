# Video Understanding

VideoNote turns an uploaded video into a structured report and preserves the
conversation grounded in that report.

## Language

**Provider**:
A named connection to an AI service, including its protocol, endpoint, credentials,
and optional transcription model.
_Avoid_: Vendor, AI service configuration

**Model configuration**:
A selectable model exposed by one provider, together with its declared capabilities
and context-window limit.
_Avoid_: Model provider, model instance

**Analysis task**:
The durable history of processing one uploaded video into a report. It remains
available even when the model configuration or prompt template it used is removed.
_Avoid_: Job, request

**Analysis mode**:
One of the fixed strategies for understanding a video: direct video, sampled frames,
or subtitles.
_Avoid_: Pipeline type

**Prompt template**:
Reusable instructions that determine the focus and structure of an analysis report.
_Avoid_: System prompt, preset

**Conversation message**:
One persisted user, assistant, or system turn in the conversation attached to an
analysis task.
_Avoid_: Chat record
