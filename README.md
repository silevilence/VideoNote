# VideoNote

个人自用、自托管的 AI 视频解读工具。上传视频后生成结构化报告，并基于报告和分段证据与 AI 对话。没有账号体系，默认在本机使用。

## 功能

- 三种理解模式：Gemini 视频直传、抽帧理解、字幕理解；无可提取字幕时可调用转写服务。
- 浏览器流式上传，默认上限 1 GiB；模型按能力筛选，支持显式手动覆盖。
- 提示词模板及本次任务内联编辑，创建时保存独立快照。
- 后台队列、SignalR 实时进度与生成文本、持久化分步日志、取消分析、Markdown 报告。
- 单 Assistant Agent 视频问答、流式回复、停止回复、成功问答历史持久化及全局对话模型设置。
- 本地 SQLite 保存配置与记录，视频和处理物料存本地；删除任务会清理物料和对话。
- 可选容器部署：仓库提供镜像构建文件与单服务 Compose 示例，镜像内含 ASP.NET Core 运行时与 FFmpeg，页面、接口与实时推送共用一个端口，数据与密钥环集中挂载；版本 tag 自动发布到 GHCR，实际验证状态见[发布说明](docs/release.md)。

## Windows 快速启动

前置：Windows 11、.NET SDK **10.0.301**（global.json 允许同 feature band 最新补丁）、FFmpeg/FFprobe **7.1.1 或支持 `stats_mux_pre` 的兼容构建**。将二者加入 PATH，或按[配置说明](docs/configuration.md)指定完整路径。首次还原 NuGet 包需要网络。

在仓库根目录的 PowerShell 执行：

```powershell
dotnet --version
ffmpeg -version
ffprobe -version
dotnet restore VideoNote.Server.sln
dotnet run --project VideoNote.Server --launch-profile http
```

打开 [VideoNote 本机页面](http://localhost:5132)。首次启动会创建工作目录、SQLite 数据库、执行迁移并预置提示词和 DeepSeek 配置。VS Code 从根目录按 F5 也会先构建再启动 Server。

进入“模型设置”：添加或编辑提供商、BaseUrl、密钥环境变量引用及模型能力。预置 DeepSeek 使用 `env:DEEPSEEK_API_KEY`，需要在**启动服务的进程环境**设置真实密钥。可用 `Read-Host -MaskInput` 输入，避免命令历史保存密钥：

```powershell
$env:DEEPSEEK_API_KEY = Read-Host -MaskInput 'DeepSeek API key'
dotnet run --project VideoNote.Server --launch-profile http
```

此示例需要 PowerShell 7。也可在设置页输入密钥，由 ASP.NET Data Protection 加密保存。接口不会返回密钥明文。预置模型名称和 128000 窗口为可编辑初值，应按提供商实际可用规格核实。

## 第一个视频与问答

1. 使用仓库内 `tests/fixtures/sample.mkv`：125 秒合成测试图、正弦音轨和软字幕，可重复生成：`pwsh -File tests/fixtures/generate.ps1`。
2. 新建任务，选择样例、**字幕理解**及已配置的文本模型，选择模板或编辑本次提示词，然后“上传并创建任务”。该样例的字幕模式无需转写。
3. 在详情页观察阶段、日志与实时文本，完成后查看 Markdown 报告。可提问“字幕描述了什么？”；刷新后问答历史仍在。
4. 验证抽帧模式时，模型需图像能力；该样例包含音轨，模型没有音频能力时还需配置转写提供商。直接模式需 Gemini 原生协议和视频能力，通过 File API 上传视频片段。

样例仅用于验证处理链路，画面与语音没有真实讲解内容。真实模型调用由所配置提供商计费；视频直传会将片段上传至 Gemini 临时存储，程序尝试及时清理。

## 发布运行

在开发机仓库根目录：

```powershell
dotnet publish VideoNote.Server -c Release -o artifacts/VideoNote
```

将 `artifacts/VideoNote` 内容复制到目标 Windows 目录。目标机安装 .NET 10 ASP.NET Core Runtime 和 FFmpeg/FFprobe，在**发布目录**打开 PowerShell：

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:DEEPSEEK_API_KEY = Read-Host -MaskInput 'DeepSeek API key'
dotnet VideoNote.Server.dll --urls http://127.0.0.1:5132
```

发布版的工作目录位于发布目录下的 `work`，与开发环境不同。默认仅绑定本机；如需通过网络访问，自行在反向代理配置访问控制、TLS、流式响应和足够的上传限制。应用自身没有登录或鉴权。

升级前停止自己的服务并备份完整 `work`（包括数据库、`keys` 和媒体）。覆盖发布文件时保留工作数据和部署配置；启动会自动迁移。勿让多个服务实例共用同一数据库，队列和运行锁设计为单实例。停机中断的分析会标记失败，尚未执行的队列可在重启后继续。

## Docker 部署示例

仓库提供 [Dockerfile](Dockerfile) 和单服务 [compose.yaml](compose.yaml)，使用本仓库 GHCR 镜像 `ghcr.io/silevilence/videonote`。推送 `V0.1.0` 形式的版本 tag 后，Actions 校验 changelog、构建并推送 `0.1.0` 镜像，再创建 GitHub Release；详见[版本发布与验证状态](docs/release.md)。须等待对应发布成功后再拉取镜像。容器启动与容器内主链路仍需按 [Docker 部署与验证](docs/docker.md) 验收。

在已安装 Docker Engine 和 Compose v2 的主机，将 `compose.yaml` 放入固定的部署目录，在该目录的 PowerShell 7 执行：

```powershell
$env:DEEPSEEK_API_KEY = Read-Host -MaskInput 'DeepSeek API key'
$env:VIDEONOTE_VERSION = '0.1.0'
docker compose pull
docker compose up -d
```

打开 [VideoNote 容器页面](http://localhost:8080)。默认仅绑定宿主机 `127.0.0.1:8080`；用 `VIDEONOTE_PORT` 修改宿主机端口，容器内部仍为 8080。前端、REST API 和 SignalR 共用此端口。Linux shell 注入环境变量的方法、持久化、升级及后续验收步骤见 [Docker 部署与验证](docs/docker.md)。

## 配置与开发验证

全部配置项、转写优先级、密钥和备份说明见[配置与运维](docs/configuration.md)。实现与验收状态见 [ROADMAP](ROADMAP.md)，本轮证据见[审核记录](docs/review-2026-09-16.md)，版本变更见 [changelog.md](changelog.md)。

```powershell
dotnet build VideoNote.Server.sln
dotnet test tests/VideoNote.Server.Tests --collect "XPlat Code Coverage" --settings tests/coverage.runsettings
```

测试使用独立工作目录和本地协议端点，包含真实 FFmpeg 和 125 秒延迟测试，不需要真实模型密钥。覆盖率统计 Server/Shared，排除 EF 迁移和模板组件；WASM 使用浏览器补充验收。

浏览器验收另需 Node.js 和 Microsoft Edge：

```powershell
npm install --prefix work-tests/browser playwright
dotnet publish VideoNote.Server -c Release -o work-tests/pipeline-publish
node tests/browser/pipeline.cjs
```

脚本启动自己的独立服务（端口 5196）和空数据库，结束后关闭测试进程。另有 `tests/browser/upload.cjs`、`settings.cjs`、`prompts.cjs` 三个脚本，需自行启动一个默认初始数据的实例并让它监听 5189（`dotnet run --project VideoNote.Server -- --urls http://localhost:5189`，或用 `VIDEONOTE_TEST_URL` 指向其他地址）；上传脚本还需先用 `pwsh -File tests/fixtures/generate-large.ps1` 生成 320 MiB 样例。真实 DeepSeek 合成物料验收命令为 `dotnet run --project tests/VideoNote.ModelProbe -- --pipeline`，需已设置 `DEEPSEEK_API_KEY`，会访问提供商并产生费用。探针关闭 SDK 自动重试，并把实际请求计数保存在 `work-tests/live-pipeline/request-count.txt`，累计最多 20 次；重新验收应先明确新的费用预算。

项目分层：`VideoNote.Client` 为 Blazor WASM 界面，`VideoNote.Server` 承载 API、SignalR、队列和 AI/FFmpeg 服务，`VideoNote.Shared` 提供共享契约和规则。分析采用固定 Map-Reduce 流程，Microsoft.Agents.AI 仅用于完成后的问答。
