# Docker 部署与验证

仓库根目录提供 `Dockerfile`、`.dockerignore` 和 `compose.yaml`，版本 tag 通过 GitHub Actions 自动构建并发布 GHCR 镜像，流程与实际验证状态见[版本发布](release.md)。开发机没有 Docker；下列 Docker 命令供具备 Docker 的部署环境使用。构建发布成功不代表容器启动、拉取及主链路验收通过。

## 镜像与运行约定

- 构建上下文为仓库根目录。SDK 阶段使用 `10.0.301-noble`，与 `global.json` 一致，先还原三个应用项目，再将 Server 发布到 `/out`；最终阶段将完整发布目录复制到 `/app`，以 `dotnet VideoNote.Server.dll` 启动。
- 运行镜像使用官方 .NET 10 ASP.NET Core Ubuntu Noble 镜像。通过发行版包管理器安装 FFmpeg 及其动态库和 FFprobe；镜像构建时运行一个小型合成音视频检查，验证 `libx264`、AAC、`stats_mux_pre` 和 FFprobe。该检查尚未在本机执行。
- 单个 ASP.NET Core 主进程提供 WASM 静态资源、REST API 和 SignalR，监听容器 HTTP 8080；媒体处理按需启动 FFmpeg/FFprobe 子进程。镜像不内置反向代理。
- 默认以官方镜像的 `app` 用户运行。镜像预建并授权 `/app/work`，首次使用空命名卷时沿用该目录权限。若改为宿主机目录绑定挂载，须由部署者预先赋予 `app` 的 UID/GID（官方 Noble 镜像为 1654:1654）读写权限；不能以符号链接替代物料目录。
- `Storage__RootPath=work` 相对于内容根 `/app` 解析；`ConnectionStrings__VideoNote=Data Source=videonote.db` 相对于工作目录解析。挂载整个 `/app/work`，而不是只挂载数据库文件：它同时包含 `videonote.db`、SQLite 辅助文件、`keys/`、`videos/`、`frames/`、`audio/`、`subtitles/`。
- 密钥只在运行时从环境变量注入。在模型设置中使用环境变量引用，预置 DeepSeek 已引用 `DEEPSEEK_API_KEY`；其他提供商需在 Compose 中另行透传所引用的变量。不要将密钥作为构建参数、Dockerfile 的 `ENV` 值或配置文件内容。`.dockerignore` 排除宿主机构建产物、工作数据、`.env`、密钥文件和环境专用 appsettings；构建所用基础 `appsettings.json` 必须保持不含密钥。

基础镜像选择依据：[.NET 10 默认使用 Ubuntu](https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/10.0/default-images-use-ubuntu)。Noble 提供 [FFmpeg 6.1.1 软件包](https://packages.ubuntu.com/noble/ffmpeg)，其 [6.1.1 源码](https://github.com/FFmpeg/FFmpeg/blob/n6.1.1/fftools/ffmpeg_opt.c)已包含应用使用的 `stats_mux_pre` 参数；这项源码核对不能替代容器实测。

## 使用已发布的 GHCR 镜像

需要 Linux 容器运行环境与 Docker Compose v2。把 `compose.yaml` 放在固定部署目录，在该目录执行。镜像地址为 `ghcr.io/silevilence/videonote`，默认版本标签为 `0.1.0`（没有 `v` 前缀）。若镜像尚未发布或当前用户没有包读取权限，拉取会失败，须先完成发布或登录有权限的 GHCR 账号。

PowerShell 7：

```powershell
$env:DEEPSEEK_API_KEY = Read-Host -MaskInput 'DeepSeek API key'
$env:VIDEONOTE_VERSION = '0.1.0'
docker compose pull
docker compose up -d
```

Linux Bash：

```bash
read -rsp 'DeepSeek API key: ' DEEPSEEK_API_KEY
echo
export DEEPSEEK_API_KEY
export VIDEONOTE_VERSION=0.1.0
docker compose pull
docker compose up -d
```

Compose 透传环境变量，不在文件中保存密钥值；更新密钥后在同一设置好变量的 shell 中运行 `docker compose up -d --force-recreate`。未设置密钥时仍可启动和管理配置，但使用相应提供商进行分析会失败。不要分享展开后的 `docker compose config` 输出，其中可能包含密钥。

| 宿主机环境变量 | 默认值 | 用途 |
|---|---|---|
| `VIDEONOTE_VERSION` | `0.1.0` | GHCR 已发布的镜像标签 |
| `VIDEONOTE_PORT` | `8080` | 宿主机端口，容器内仍为 8080 |
| `VIDEONOTE_BIND_ADDRESS` | `127.0.0.1` | 宿主机绑定地址 |
| `DEEPSEEK_API_KEY` | 不设默认值 | 透传给容器，供预置提供商在调用时解析 |

打开 [本机入口](http://localhost:8080)（修改端口后使用对应地址）。首次启动自动初始化工作目录、迁移数据库并写入默认配置。运行状态和日志可用 `docker compose ps`、`docker compose logs --tail 100 videonote` 查看。

如需局域网访问，可将 `VIDEONOTE_BIND_ADDRESS` 设为指定网卡地址或 `0.0.0.0`。应用没有登录或鉴权，远程部署需在应用外提供访问控制及 TLS；反向代理应支持 WebSocket/长轮询、流式回复及足够的上传大小限制，详见[配置与运维](configuration.md)。

## 持久化、升级与备份

Compose 固定项目名称为 `videonote`，默认创建命名卷 `videonote_videonote-data`。保持项目名称和卷配置不变；不要使用 `--scale` 扩副本，也不要让不同项目或容器同时使用此数据库。

升级前先 `docker compose stop`，在容器停止后通过部署环境的卷备份工具备份整个数据卷（包含 `keys` 及媒体），并记录当前镜像版本。然后设置新的 `VIDEONOTE_VERSION`，在已注入密钥的 shell 中执行 `docker compose pull` 和 `docker compose up -d`。启动时自动迁移数据库；回退旧镜像不等于回退数据库，必要时须恢复升级前的整卷备份。

普通 `docker compose down` 会移除容器但保留命名卷；**不要使用 `docker compose down -v`**，它会删除数据卷。若由已有 Windows 部署迁移，须停服后复制完整 `work`，修正容器目录权限，并在 Linux 环境另行验证旧任务物料访问与密钥解密。

## 本次本机验证（2026-09-16）

- `dotnet publish VideoNote.Server -c Release -o work-tests/docker-publish /p:UseAppHost=false` 退出码 0；核对入口 DLL、运行时配置（`net10.0`）、静态资源清单、WASM 文件、上传脚本及 Linux x64/ARM64 SQLite 原生库存在。
- 使用 `dockerfile-ast` 解析 Dockerfile，核对两个阶段、源文件路径、`/out` 到 `/app` 的复制、入口、非 root 用户和 8080 端口，全部通过。
- 使用 PyYAML 解析 Compose，核对单服务、单端口映射、GHCR 地址、卷挂载、无默认密钥值的环境透传；应用环境变量名称与 `appsettings.json` 对照通过。
- 使用 `@balena/dockerignore` 检查构建上下文：项目源文件、基础配置与静态资源保留；宿主机 `bin/obj`、工作数据、密钥环、`.env`、证书及环境专用配置排除，全部通过。验证工具仅安装在忽略的 `work-tests/docker-validation`。
- 对两个基础镜像标签执行 MCR manifest HEAD 请求，均返回 HTTP 200；这只验证标签存在，没有拉取镜像。
- `git diff --check` 通过。本次未改应用逻辑，未调用真实模型；未执行 Docker 构建、启动、Compose CLI 校验、GHCR 发布或容器主链路验收。

## 后续 Docker 环境验收（未执行）

在仓库根目录执行构建并检查镜像依赖：

```bash
docker build --pull -t videonote:verify .
docker run --rm --entrypoint dotnet videonote:verify --list-runtimes
docker run --rm --entrypoint ffmpeg videonote:verify -version
docker run --rm --entrypoint ffprobe videonote:verify -version
```

主链路验收应使用独立测试数据卷和测试密钥，避免修改已有部署：

```bash
docker run -d --name videonote-verify -p 127.0.0.1:18080:8080 \
  --mount type=volume,source=videonote-verify-data,target=/app/work \
  -e DEEPSEEK_API_KEY videonote:verify
```

1. 浏览 [测试入口](http://localhost:18080)，检查首页、WASM 资源与模型设置接口；日志应无数据库、密钥环或目录权限错误。
2. 按 README 使用 `tests/fixtures/sample.mkv` 完成上传 → 字幕理解 → 实时进度 → 报告，确认 SignalR 与 API 同端口工作；模型调用会产生费用。再验证抽帧与视频直传时，需对应图像/视频模型及转写配置。
3. 停止并删除该测试容器，再用上面的相同命名卷重新创建；确认提供商、报告、媒体和 `keys` 仍在，历史报告可访问。
4. 镜像实际发布后，在独立部署目录执行 `docker compose config --quiet`、`docker compose pull` 和 `docker compose up -d`，验证默认 8080 及自定义宿主机端口均可访问，记录镜像标签、构建日志与验收结果。

本机的 `dotnet publish` 与静态配置核对只能证明发布文件与配置的对应关系，不能据此认定上述容器验收通过。
