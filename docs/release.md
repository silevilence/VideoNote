# 版本 Tag 自动发布

2026-09-17 已确认：历史 `V0.1.0` 构建与推送虽成功，其 `0.1.0/latest` 镜像遗漏 `blazor.web.js`，导致首页空白。修复代码及本机发布启动回归已完成，修复镜像尚未发布，详情见 [Docker 故障排查](docker.md#首页空白与启动脚本-404)。下文历史构建成功记录不代表该镜像的页面可用性通过。

`.github/workflows/release.yml` 自动发布只监听三段数字版本 tag，例如 `V0.1.0`、`v0.1.0`、`V12.34.56`。普通分支推送、其他 tag 与预发布后缀不触发。大小写前缀等价，同一版本只推送一种写法。另提供手动入口，将已发布版本的镜像复制为 `latest`，不执行构建或创建 Release。

## 发布流程

1. 检出 tag 指向的提交，运行 `scripts/release-notes.py`，从该提交的 `changelog.md` 提取匹配的 `## V<版本>` 段落（忽略大小写，包含标题，到下一个二级标题前结束）。缺失、空白或重复段落以非零码失败，错误指出版本号，后续步骤不运行。
2. 使用现有 Dockerfile 构建 `linux/amd64` 镜像，推送 `ghcr.io/silevilence/videonote:<版本>`，去掉 tag 的 `V`/`v` 前缀。添加来源仓库、提交与版本 OCI 标签；不改 Dockerfile、Compose、内部 8080 端口与数据卷约定。
3. 推送镜像成功后，以原始 tag 为名称创建 GitHub Release，说明直接使用提取的段落。`--verify-tag` 要求远端 tag 已存在。
4. 正式发布成功后，为该版本镜像更新 `latest` 别名，并比较两个标签的 manifest 内容必须一致。发布与手动补标签共用并发组，避免同时修改 `latest`。

工作流只使用自动提供的 `GITHUB_TOKEN`，权限为 `contents: write`（创建 Release）、`packages: write`（推送 GHCR），无需添加仓库 Secret。仓库必须允许 GitHub Actions；若同名 GHCR 包已存在，它必须允许本仓库 Actions 写入。GHCR 包可见性单独管理，首次发布不保证匿名可拉取；私有包需部署者登录有读取权限的 GHCR 账号，见 [GitHub 容器注册表说明](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)。

## 维护者操作

先同步 `VideoNote.Server/VideoNote.Server.csproj` 的 `<Version>` 与 `changelog.md` 对应段落，再从仓库根目录运行（Python 3.10+，不需要 Docker）：

```powershell
python -m unittest discover -s tests/release-tests -v
python scripts/release-notes.py V0.1.0 --output work-tests/release/V0.1.0.md
python scripts/release-notes.py V9.9.9 --output work-tests/release/missing.md
```

前两条应成功，第三条应以退出码 1 报告缺失 `9.9.9`，不创建说明文件。回归覆盖大小写、多个版本段落边界、BOM/CRLF、非法 tag、缺失/重复/空白段落与 Actions 输出值。

提交已确认的发布内容并推送分支后，创建并单独推送本次 tag：

```powershell
git tag V0.1.0
git push origin V0.1.0
gh run list --workflow release.yml
gh release view V0.1.0
```

在 [Actions](https://github.com/silevilence/VideoNote/actions/workflows/release.yml) 等待流程完成，再按 [Docker 部署](docker.md) 拉取对应版本。Compose 默认使用 `latest`，它指向最近一次成功发布或手动指定的正式版本；补发旧版本也会更新此别名。需要固定版本时设置 `VIDEONOTE_VERSION=0.1.0`。

补充或修复 `latest` 时，运行以下命令（`version` 必须是已存在的三段数字镜像版本，不带 `v`）：

```powershell
gh workflow run release.yml --ref main -f version=0.1.0
```

该入口只运行 `latest` 任务，使用 [Buildx imagetools](https://docs.docker.com/reference/cli/docker/buildx/imagetools/create/) 的 `--prefer-index=false` 复制现有 manifest，保留镜像内容和摘要，不重新构建、不创建版本或移动 Git tag。`latest` 更新失败时可用此入口重试，无需重跑已成功的 Release 创建步骤。

若构建或推送失败，不创建 Release；若仅 Release 创建失败，已推送的镜像会保留。基础设施临时故障可在 Actions 重跑失败任务；已发布成功的版本不要移动或覆盖 tag，修复发布内容应使用新版本。Release 已存在时创建步骤会失败，不自动覆盖说明。

## 验证记录（2026-09-16）

- `python -m unittest discover -s tests/release-tests -v`：5 组回归全部通过（含多个 tag 与无效段落子用例）；真实 `V0.1.0` 提取成功，`V9.9.9` 退出码 1 且指出缺失版本。
- `actionlint 1.7.12` 工作流语法校验通过（未安装 ShellCheck/Pyflakes，关闭这两项外部检查）；`git diff --check` 通过，Dockerfile 与 compose.yaml 无改动。
- 已推送 `main` 与 `V0.1.0`，发布提交为 `078893258ae7c782289b9cca6bd7c857065f32dc`。[Actions 运行 35067137232](https://github.com/silevilence/VideoNote/actions/runs/35067137232) 于 2026-09-16 07:11 UTC 成功完成：tag 触发 → changelog 校验 → 构建 → GHCR 登录 → 推送 → Release 创建，全部步骤成功。
- 镜像为 `ghcr.io/silevilence/videonote:0.1.0`（Linux amd64），推送摘要为 `sha256:ad38374c55f9e55742004154808b611d752f6c2d6aacdb784e1fdb20bde63fe4`。Dockerfile 中 FFmpeg 合成视频、`stats_mux_pre` 与 FFprobe 自检成功，日志输出 H.264 与 AAC 编码。
- 匿名请求 GHCR `0.1.0` manifest 返回 HTTP 200，摘要与推送日志一致；这是注册表元数据访问验证，没有下载镜像层或启动容器。
- [V0.1.0 Release](https://github.com/silevilence/VideoNote/releases/tag/V0.1.0) 已正式发布（非草稿、非预发布），通过 API 读取正文并与本地提取结果比较，一致。
- 本机无 Docker；容器启动、Compose 拉取与容器内分析/对话主链路未验证。本次发布任务不调用真实模型。

### latest 补标签验收（2026-09-16）

- [Actions 运行 35068384817](https://github.com/silevilence/VideoNote/actions/runs/35068384817) 使用手动入口 `version=0.1.0`，仅运行 `latest` 任务，构建与 Release 任务跳过；manifest 复制与内容比较通过。
- 匿名访问 `0.1.0` 与 `latest` manifest 均返回 HTTP 200，两者摘要同为 `sha256:ad38374c55f9e55742004154808b611d752f6c2d6aacdb784e1fdb20bde63fe4`。没有重新构建镜像，`V0.1.0` Git tag 仍指向 `078893258ae7c782289b9cca6bd7c857065f32dc`。
- Compose 默认值已改为 `latest`，保留 `VIDEONOTE_VERSION` 覆盖。PyYAML 解析与默认值核对、actionlint 工作流语法检查通过；本机未运行 Docker Compose CLI。

工作流语法与权限依据：[GitHub Actions 工作流语法](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)、[发布 Docker 镜像](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images)、[gh release create](https://cli.github.com/manual/gh_release_create)。
