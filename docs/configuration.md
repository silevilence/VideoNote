# 配置与运维

服务按 ASP.NET Core 配置顺序读取 `appsettings.json`、`appsettings.{Environment}.json`、环境变量及命令行。后者覆盖前者；环境变量使用双下划线，例如 `Ffmpeg__FfmpegPath`。以下默认值均来自当前代码与 Server 配置。

## 配置项

| 配置键 | 默认值 | 含义与约束 |
|---|---|---|
| ConnectionStrings:VideoNote | Data Source=videonote.db | 相对数据库路径在 Storage 根目录下解析；也可用绝对路径 |
| Storage:RootPath | work | 必须为服务端内容根目录下的相对路径，不接受绝对路径和越界路径 |
| Storage:VideosDirectoryName | videos | 上传视频和分段视频；相对工作目录 |
| Storage:FramesDirectoryName | frames | 抽帧物料；相对工作目录 |
| Storage:AudioDirectoryName | audio | 音频物料；相对工作目录 |
| Storage:SubtitlesDirectoryName | subtitles | 字幕与预处理清单；相对工作目录 |
| Upload:MaxBytes | 1073741824 | 单个视频 1 GiB，必须大于零；服务端实际读取时也计数 |
| Upload:AllowedExtensions | .mp4,.mkv,.mov,.webm,.avi,.m4v | JSON 字符串数组，必须带点，不区分大小写；自定义数组替换默认值 |
| Ffmpeg:FfmpegPath | ffmpeg | FFmpeg 程序名或完整路径 |
| Ffmpeg:FfprobePath | ffprobe | FFprobe 程序名或完整路径 |
| Ffmpeg:SegmentSeconds | 60 | 分段秒数，大于零 |
| Ffmpeg:OverlapSeconds | 5 | 重叠秒数，非负且小于分段秒数 |
| Ffmpeg:FramesPerSecond | 1 | 抽帧率，0 到 60 之间、不含 0 |
| Ffmpeg:TimeoutSeconds | 900 | 单次媒体进程超时，大于零 |
| Analysis:MaxConcurrency | 1 | 当前只支持 1；其他值启动校验失败 |
| Pipeline:MaxOutputTokens | 2048 | 分段/报告输出上限，至少 128；实际按上下文预算约束 |
| Pipeline:MaxImagesPerSegment | 8 | 每段图片数上限，大于零 |
| Pipeline:ImageTokenEstimate | 2048 | 每图保守 token 估算，大于零 |
| Pipeline:AudioTokensPerSecond | 64 | 每秒音频 token 估算，大于零 |
| Pipeline:VideoTokensPerSecond | 512 | 每秒视频 token 估算，大于零 |
| Pipeline:RequestTimeoutSeconds | 300 | 单次理解/组合请求超时，大于零 |
| Transcription:ProviderId | null | 全局转写提供商 GUID，需为 OpenAI 兼容协议 |
| Transcription:Model | null | 全局转写模型兜底名称 |
| Transcription:TimeoutSeconds | 600 | 单次转写 HTTP 请求超时，大于零 |
| Transcription:MaxAudioBytes | 26214400 | 单个转写音频上限 25 MiB，大于零 |
| Conversation:MaxOutputTokens | 2048 | 对话输出上限，至少 128 |
| Conversation:RequestTimeoutSeconds | 300 | 单轮对话（含保存）时限，大于零 |
| Logging:LogLevel:Default | Information | 应用日志等级 |
| Logging:LogLevel:Microsoft.AspNetCore | Warning | ASP.NET Core 日志等级；开发配置可覆盖 |
| AllowedHosts | * | ASP.NET Host 筛选，不能替代访问控制 |

存储目录必须可写；任务物料操作拒绝符号链接/目录联接。程序还在 `work/keys` 保存 Data Protection 密钥环。开发环境内容根为 `VideoNote.Server`；发布后从发布目录启动，以该目录为内容根。

示例（启动前在当前 PowerShell 会话设置，不包含任何密钥）：

```powershell
$env:Ffmpeg__FfmpegPath = 'C:\Tools\ffmpeg\bin\ffmpeg.exe'
$env:Ffmpeg__FfprobePath = 'C:\Tools\ffmpeg\bin\ffprobe.exe'
$env:Storage__RootPath = 'work-local'
$env:Pipeline__RequestTimeoutSeconds = '600'
$env:Conversation__RequestTimeoutSeconds = '600'
```

## 模型与转写选择

提供商和模型保存在 SQLite，使用“模型设置”管理，不是在 appsettings 中维护模型列表。分析创建时必须选模型。直接模式过滤视频能力，抽帧过滤图像能力，字幕模式接受文本模型；手动覆盖只绕过能力筛选，不保证上游支持该输入。

DeepSeek 通过一次性迁移预置：OpenAI 兼容、`https://api.deepseek.com`、`deepseek-v4-flash-vision-exp`、图像和流式能力、环境变量引用 `DEEPSEEK_API_KEY`。已有同名/同地址提供商保持原样；删除预置项后重启不会复建。上下文 128000 为表单初值，不代表核实过的模型规格。

转写选择顺序：分析提供商为 OpenAI 兼容且设置了转写模型时优先使用它；否则使用 `Transcription:ProviderId` 指定的提供商；未指定全局提供商则回退当前分析提供商。模型名称优先使用最终提供商的转写模型，缺失时使用 `Transcription:Model`。服务必须支持 `/audio/transcriptions`、`verbose_json` 及分段时间戳。

例如先在设置页建立转写提供商（BaseUrl 应包含需要的版本前缀，如 `https://api.example.com/v1`），然后从 `GET /api/providers` 获取其 id，配置 `Transcription__ProviderId`；可在该提供商设置转写模型，也可配置 `Transcription__Model`。修改进程环境后重启自己的服务。

字幕模式优先提取可用文本软字幕；没有时转写音轨。无字幕且无音轨会明确失败。抽帧模式有音轨时，模型支持音频则直接输入，否则调用转写；无音轨仍可分析画面。v1 未实现本地 Whisper。

全局对话模型在设置页选择并保存，未指定则采用任务分析模型；模型删除后外键置空。问答使用完整报告、全部分段和成功对话历史，不静默删减；保守预算超过配置的上下文窗口时，请换更大窗口模型或校准窗口配置。非流式模型会等待完整回复再展示。流式中断、过滤、长度截断不作为成功问答保存。

## 密钥、备份与错误恢复

- 推荐环境变量引用：在提供商表单填写环境变量名称，服务在实际调用时解析。不要把密钥写入仓库、appsettings、截图或日志。`.env` 不会由程序自动加载。
- 直接输入的密钥经 ASP.NET Data Protection 保护，列表/详情不回传明文。空密钥输入表示保留原值，清除使用表单专用选项。备份时必须连同 `work/keys` 一起保留；同时持有数据库和密钥环的进程可能解密密钥，应限制目录访问权限。
- 数据库相对路径默认是 `work/videonote.db`。如自行改成绝对路径，备份还需包含该文件。为保持 SQLite 与媒体一致，先正常停止自己的服务，再复制整个工作目录；不要直接覆盖正在写入的数据库。
- 运行中任务先取消再删除；后台尚未退出或问答仍在生成时删除返回冲突，等待或停止回复后重试。删除提供商会删除其模型，历史任务保留，关联外键清空。
- 分析模型未配置/已删除、转写服务缺失、无音轨、上游失败和超时均在详情显示明确错误。失败后调整配置并新建任务；当前没有原任务重跑入口。
- 浏览器断线可重新进入详情；最终报告、完整分段、日志和成功问答从数据库恢复。未完成的实时文本不保证刷新后保留。分析状态通知异常不会覆盖已完成的分析结果。
- 自托管只有单实例模式，无账号体系；如暴露到局域网或互联网，需在应用外提供鉴权与 TLS。反向代理必须允许大请求体，并对 `/api/tasks/*/conversation` 关闭响应缓冲；SignalR 需支持 WebSocket 或长轮询。
