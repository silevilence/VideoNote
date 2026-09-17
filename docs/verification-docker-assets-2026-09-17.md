# Docker 发布文件完整性复查（2026-09-17）

## 结论

检查 GHCR 原 `0.1.0/latest` 镜像应用层与当前修复后的干净发布产物，确认旧镜像遗漏以下 6 个文件，修复后均已补齐：

```text
wwwroot/_framework/blazor.web.js
wwwroot/_framework/blazor.web.js.gz
wwwroot/_framework/blazor.web.js.br
wwwroot/_framework/blazor.server.js
wwwroot/_framework/blazor.server.js.gz
wwwroot/_framework/blazor.server.js.br
```

这些文件来自同一个 `Microsoft.AspNetCore.App.Internal.Assets` 包。当前应用使用 Interactive WebAssembly，首页实际依赖 `blazor.web.js`；`blazor.server.js` 是该包同时提供的 Server 模式脚本，不是此次页面的启动入口。

除以上文件外，没有发现其它发布文件遗漏。两次构建的 `VideoNote.Client`、`VideoNote.Shared` WASM 文件指纹发生变化，各自的原文件和压缩文件均存在，并通过当前发布清单与浏览器加载校验；不能将旧指纹文件名未出现在新发布目录视为缺失。

## 检查范围与结果

原镜像应用层：`sha256:cc05cdcfe75bb88c343a0b06998b439f4969fe1975a026b178cd4ffc1540489e`。本次没有运行 Docker；从该层提取应用文件进行比对。修复产物在本机 Windows/.NET SDK 10.0.301 上构建。

按 `.dockerignore` 实际规则筛选源码，使用全新目录执行与 Dockerfile 相同的顺序：只复制 csproj/global.json → restore → 复制源码 → publish --no-restore。共纳入 109 个当前源码文件，仅排除了 3 个开发配置文件（Server/Client 的 `appsettings.Development.json`、Server 的 `launchSettings.json`）。基础 `appsettings.json` 保留。

| 检查 | 结果 |
|---|---|
| csproj-only 与完整源码分别 restore | 两者均为相同的 61 项依赖，无差异 |
| 静态资源清单与磁盘文件 | 修复后 235 个文件全部存在；旧镜像清单的 229 个文件也全部存在，但它的清单本身漏掉了上述资源 |
| 静态文件长度与 SHA-256 ETag | 所有清单项一致 |
| WASM 文件 | 65 个，全部存在 |
| gzip/Brotli 压缩文件 | 全部能解压，内容与对应原文件一致 |
| 所有静态资源路由及编码变体 | 467 个 URL、777 种响应全部 HTTP 200；类型、编码、解压后的响应内容均与文件一致 |
| Server `.deps.json` 运行依赖 | 40 项 runtime、24 项 runtimeTargets 文件全部存在，包括 Linux x64 SQLite 原生库 |
| 入口 DLL、runtimeconfig、deps、静态资源清单、基础配置 | 全部存在；服务启动和数据库初始化成功 |
| 首页、任务列表、新建任务、模型设置、提示词模板、404 页面 | 浏览器渲染通过，无同源资源请求错误 |
| 上传、三种分析模式、报告、流式问答及历史读取 | 发布产物浏览器回归通过；真实 FFmpeg + 本地协议模拟端点，不消耗真实模型请求 |

完整分析回归还覆盖取消、元数据重试、转写失败、缺少字幕/音轨、缺少模型和桌面/移动布局。`upload.js`、`chat.js` 的按需加载通过实际操作验证。

## 可重复检查

```powershell
python -m unittest discover -s tests/deployment -v
dotnet publish VideoNote.Server -c Release -o work-tests/startup-publish
node tests/browser/startup.cjs work-tests/startup-publish
$env:VIDEONOTE_PUBLISH_DIR = (Resolve-Path work-tests/startup-publish).Path
node tests/browser/pipeline.cjs
```

`startup.cjs` 已扩展为逐项检查静态清单响应、压缩内容和主要页面。`pipeline.cjs` 可通过 `VIDEONOTE_PUBLISH_DIR` 指定待验收产物，默认目录保持不变。浏览器测试依赖仓库约定的 Playwright、Edge；管线测试还需要 FFmpeg。

本次详细对比记录位于忽略目录 `work-tests/asset-audit/`（`inventory.json`、`context-files.json`、`pipeline.log`）。这些结果不能替代 Linux 容器启动、FFmpeg 动态库加载、文件卷权限和 NAS 更新后的验收；修复镜像是否发布应以发布记录为准。
