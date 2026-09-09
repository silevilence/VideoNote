# AGENTS.md

## 项目概述

VideoNote — AI 视频解读工具（个人自用/自托管，无账号体系）。上传视频后，通过 AI 模型的三种理解模式生成结构化解读报告，并可基于报告与分段理解结果与 Agent 对话问答。

## 当前状态

项目已完成三层骨架、核心数据模型，以及提供商/模型配置 API 与 UI、AI 客户端工厂、FFmpeg 服务、流式上传存储、提示词模板六项基础功能。前端已按已实现功能完成正式界面重设计（暗色放映室设计系统）：总览仪表盘、新建任务（拖拽上传 + 按模式过滤模型能力）、任务列表与详情页、模型设置、提示词模板；分析管线尚未接入，任务创建后仅保存视频与配置，详情页有明确边界说明。后续继续按 `ROADMAP.md` 计划中条目的依赖顺序推进；本轮审核记录见 `docs/review-2026-09-09.md`。

## 技术栈（已确认决策）

- **框架**：.NET 10，Blazor Web App + Interactive WebAssembly render mode，三层结构 Client（WASM）/ Server / Shared
- **Server**：ASP.NET Core（REST API + SignalR）+ EF Core + SQLite
- **模型交互**：Microsoft.Extensions.AI（`IChatClient` 统一抽象），适配器覆盖 OpenAI 兼容协议与 Google Gemini 原生协议两种
- **Agent 编排**：Microsoft.Agents.AI — 仅用于「理解报告生成后的对话问答」；任务管道本身不用 Agent 拓扑，由 C# 固定流程编排
- **视频处理**：FFmpeg（分段、抽帧、音频提取、软字幕提取）
- **转写**：OpenAI 兼容 `/audio/transcriptions` 端点（v1 不做本地 Whisper，预留后端接口）

## 关键架构决策
- **任务管道**（三种模式：直接理解/抽帧理解/字幕理解）均为固定流程 Map-Reduce：预处理 → 逐段理解 → 组合报告。用 Channel + BackgroundService 异步执行，SignalR 同时推送进度事件与流式文本事件。
- **任务状态机**：排队 → 预处理 → 理解 → 组合 → 完成/失败/取消。取消态已随数据模型落地（`AnalysisTaskStatus.Canceled`，运行中任务删除前置校验使用），取消事件推送由后台任务框架实现。
- **模型配置两级**：提供商（名称、协议类型、BaseUrl、ApiKey、可选转写模型）→ 模型（ModelId、能力标记、上下文窗口）。能力标记：思考、工具使用、流式、多模态（图像/音频/视频）。
- **模型选择规则**：按模式自动过滤候选模型（直接理解→视频能力；抽帧→图像能力；字幕→文本模型；音频直传→音频能力），**允许手动覆盖**。
- **Agent 对话上下文** = 最终报告 + 全部分段理解文本，一次注入；对话消息持久化到 SQLite。
- **存储**：视频与处理物料存本地工作目录，元数据存 SQLite。本机 Windows 运行，不引入容器化依赖。
- **历史保留策略**：删除提供商时级联删除其模型配置；模型配置或提示词模板删除时，历史分析任务保留且对应外键置空；删除分析任务时级联删除其对话消息。
- **本地目录约定**：服务端内容根目录下使用 `work/videos`、`work/frames`、`work/audio`、`work/subtitles`，SQLite 数据库位于 `work/videonote.db`。

## 已知约束

- **直接理解模式**（视频直传）当前实际可用模型限于 Gemini 系——OpenAI 兼容端点基本不支持视频输入，这正是「视频能力」标记必须存在的原因。
- 视频直传须走 Gemini File API（临时存储，约 2 天有效），不能内联传大文件。
- 视频上传必须是流式的（WASM 客户端内存有限），服务端校验扩展名与大小上限。
- ApiKey 不得在配置列表/详情接口明文返回。

## 环境事实（当前开发机）

- Windows 11；.NET SDK 10.0.301（另有 6.0/8.0/9.0）
- FFmpeg 7.1.1（gyan.dev full build）已在 PATH
- Docker 不可用，按本机 Windows 运行设计（ASP.NET Core 跨平台，迁移时仅需调整 FFmpeg 路径配置）
- VS Code 从仓库根目录按 F5：先构建 Server 项目，再使用现有 `http` 启动配置运行并打开浏览器；工作目录为 `VideoNote.Server`。

## 开发约定

- 每条任务的功能要求与验收标准以 `ROADMAP.md` 对应条目为准，实现完成后按验收标准逐条验证。
- 需求存在歧义时先与用户核对（采用对话确认制），不擅自扩大范围。
- 新确认的需求决策先回写 ROADMAP/本文档，再进入实现。
