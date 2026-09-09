# 本轮验证记录

## 2026-09-09 前端界面改版

- 设计系统：`VideoNote.Server/wwwroot/app.css` 重写为暗色放映室主题（自建设计令牌与组件样式），`App.razor` 移除 Bootstrap CSS 引用，改为 Sora / IBM Plex Mono 网络字体（离线回退系统中文字体）。布局壳与导航移入 `MainLayout.razor` / `NavMenu.razor`，删除原隔离样式文件。
- 页面结构：首页改总览仪表盘（统计 + 最近任务 + 配置就绪度）；任务拆为 `/tasks/new`（拖拽上传、模式卡片、按模式能力过滤模型、可勾选显示全部手动覆盖）、`/tasks`（状态筛选、时间码、删除）与 `/tasks/{id}` 详情页（任务信息、阶段、提示词快照、报告区明确标注管线未接入）；设置页与提示词页改卡片式管理，表单组件统一新样式。上传成功后跳转详情页。删除 `Counter.razor`、`Weather.razor`。
- 顺带修复：`UploadOptions.AllowedExtensions` 预置默认值导致配置绑定追加、`/api/tasks/upload-limits` 返回双份扩展名（选项默认改为空数组，由 appsettings 提供；两处直接构造 `UploadOptions` 的测试改为显式传扩展名）。
- 验证：`dotnet test -c Release` 39/39 通过；`dotnet publish -c Release` 后 Production 独立实例（`Storage__RootPath` 指向发布目录内隔离目录）依次通过 `tests/browser/settings.cjs`、`prompts.cjs`、`upload.cjs`（含 320 MiB 原生上传），无 pageerror；Playwright 全页截图复核桌面与移动端布局。
- 复现：与既有浏览器流程相同，先 `npm install --prefix work-tests/browser playwright`，从发布目录启动服务到 localhost:5189 后依次 `node tests/browser/*.cjs`。截图存于 `work-tests/browser/shots/`。

## 逐项快速审核
- 配置 API：HTTP CRUD、能力字段存取、密钥不回显、加密保存、环境变量引用、级联删除与历史保留通过。
- 设置 UI：Edge 无头浏览器完成两级新增/编辑/刷新/删除，无 pageerror；零警告构建。
- AI 工厂：Microsoft.Extensions.AI.OpenAI 10.9.0 与 Google.GenAI 1.21.0 官方适配器。统一 IChatClient 调用通过两种协议文本/流式、图像、取消、动态配置测试；Gemini 音频/视频文件引用通过。禁止内联视频，后续管线应先调用 Gemini File API。
- DeepSeek 真实验证：指定 deepseek-v4-flash-vision-exp 在 https://api.deepseek.com 的非流式、流式和红色测试图片识别通过。仅程序在调用时读取 DEEPSEEK_API_KEY，工具未读取或输出密钥值。
- Gemini 真实验证：按本轮确认使用协议模拟验收；待提供真实配置后补做，不宣称已真实调用。
- 回归修复：存储与 SQLite 配置改为 DI 解析最终配置；接口测试断言独立 work-tests 目录，避免重复测试污染默认数据库。已清除本次产生的三条测试历史记录。

## 复现
- 单元/接口/协议测试：dotnet test tests/VideoNote.Server.Tests
- 真实 DeepSeek：dotnet run --project tests/VideoNote.ModelProbe -- <红色 PNG 路径>
  服务进程环境需设置 DEEPSEEK_API_KEY；验证程序不打印配置、请求头或原始异常。
- 浏览器：npm install --prefix work-tests/browser playwright；以独立工作目录启动开发服务到 localhost:5189；node tests/browser/settings.cjs。
  测试创建与删除自己的 UI Smoke 配置，应针对空的独立测试数据库运行。
- 源码运行使用 Development 环境；Production 环境使用 dotnet publish 输出。

## SDK 依据
- https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI/10.9.0
- https://googleapis.github.io/dotnet-genai/api/Microsoft.Extensions.AI.GoogleGenAIExtensions.html
- https://googleapis.github.io/dotnet-genai/api/Google.GenAI.Types.HttpOptions.html

## FFmpeg 验证
- 仓库样例 tests/fixtures/sample.mkv：125 秒、160×90、5 fps，含音轨和两条软字幕，约 1.5 MB；可运行 tests/fixtures/generate.ps1 重建。
- 真实 FFmpeg 测试通过：3 段，起点 0/55/110 秒；段时长误差 ≤0.3 秒；约 125 帧及毫秒时间戳；16 kHz 单声道 WAV 和 MP3；两条 SRT 内容、无字幕分支和取消/缺失程序提示。
- Ffmpeg 配置见 appsettings.json：分段时长必须大于重叠，帧率 (0,60]，超时单位秒。文件参数通过 ArgumentList 传递，不执行 shell 拼接。

## 上传与存储验证
- 20 个测试通过；320 MiB 有效 MP4 原生浏览器上传成功，服务端集成测试验证 SHA-256 完全一致。
- 文件通过 XMLHttpRequest.send(File) 从浏览器直接上传，不经 WASM 字节数组；服务端使用 64 KiB 缓冲、逐块计数和临时文件，大小默认 1 GiB。
- 非法扩展名、路径穿越文件名、空文件、已知/未知长度超限、中断与取消均无残留任务/临时文件。
- 删除任务清理四类工作目录及对话记录；拒绝删除运行中任务、符号链接和目录联接。文件占用导致清理失败时保留任务记录以便重试。
- 浏览器真实上传脚本：先运行 tests/fixtures/generate-large.ps1，再 node tests/browser/upload.cjs；大样例位于忽略的 work-tests。

## 提示词模板验证
- 21 个测试通过；模板由固定 ID 的迁移初始化，反复启动不重复添加。
- 内置大纲/重点/摘要/关键词只读；复制产生独立自建模板。API 与真实浏览器完成创建、查看、编辑、删除、刷新持久化。
- 上传页可选任意模板并预览；任务保存 PromptContentSnapshot。模板修改/删除后历史快照保持不变，删除外键置空。
- 浏览器复现：node tests/browser/prompts.cjs，验证内置保护、复制、模板选择与删除后快照。

## 最终回归
- 39 个测试通过，0 失败、0 跳过。
- 覆盖率：dotnet test tests/VideoNote.Server.Tests --collect "XPlat Code Coverage" --settings tests/coverage.runsettings --results-directory work-tests/coverage-final
- 服务端/共享代码行覆盖率 97.86%（686/701），分支覆盖率 85.19%；客户端单独通过三组真实浏览器验证，不包含在该覆盖率中。
- dotnet publish VideoNote.Server -c Release -o work-tests/publish 成功；从该独立发布目录启动 Production 服务后，三个浏览器脚本全部通过。
- dotnet ef migrations has-pending-model-changes --project VideoNote.Server --no-build：无待生成迁移。
- 整体审核修复与剩余验证边界见 review-2026-09-09.md。

## 使用本轮功能
1. 在仓库根目录运行 dotnet run --project VideoNote.Server --launch-profile http，然后打开启动日志中的本地地址。
2. 模型设置 → 新建提供商：名称 DeepSeek，协议 OpenAI 兼容，Base URL 为 https://api.deepseek.com，密钥环境变量名称为 DEEPSEEK_API_KEY；API Key 输入留空。
3. 在该提供商下新建模型：Model ID 为 deepseek-v4-flash-vision-exp，勾选图片和流式。上下文窗口按实际模型规格填写，表单默认值不是对模型规格的声明。
4. 在提示词模板页复制内置模板或创建自定义模板；上传验证页可选择模型、模式和模板，保存视频并验证持久化及删除。
5. 服务进程需要继承所配置的环境变量。更改 Windows 环境变量后，重新启动服务进程。程序不在 API 列表/详情中返回密钥。
6. 数据库和媒体在服务端 work 目录；使用直接输入的密钥时，还需要保留 work/keys 才能解密已有配置。该目录应作为本地应用数据管理，不纳入 Git。
