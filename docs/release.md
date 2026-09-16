# 版本 Tag 自动发布

`.github/workflows/release.yml` 只监听三段数字版本 tag，例如 `V0.1.0`、`v0.1.0`、`V12.34.56`。普通分支推送、其他 tag 与预发布后缀不触发。大小写前缀等价，同一版本只推送一种写法。

## 发布流程

1. 检出 tag 指向的提交，运行 `scripts/release-notes.py`，从该提交的 `changelog.md` 提取匹配的 `## V<版本>` 段落（忽略大小写，包含标题，到下一个二级标题前结束）。缺失、空白或重复段落以非零码失败，错误指出版本号，后续步骤不运行。
2. 使用现有 Dockerfile 构建 `linux/amd64` 镜像，推送 `ghcr.io/silevilence/videonote:<版本>`，去掉 tag 的 `V`/`v` 前缀。添加来源仓库、提交与版本 OCI 标签；不改 Dockerfile、Compose、内部 8080 端口与数据卷约定。
3. 推送镜像成功后，以原始 tag 为名称创建 GitHub Release，说明直接使用提取的段落。`--verify-tag` 要求远端 tag 已存在。

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

在 [Actions](https://github.com/silevilence/VideoNote/actions/workflows/release.yml) 等待流程完成，再按 [Docker 部署](docker.md) 拉取对应版本。工作流不维护 `latest` 镜像标签。

若构建或推送失败，不创建 Release；若仅 Release 创建失败，已推送的镜像会保留。基础设施临时故障可在 Actions 重跑失败任务；已发布成功的版本不要移动或覆盖 tag，修复发布内容应使用新版本。Release 已存在时创建步骤会失败，不自动覆盖说明。

## 验证记录（2026-09-16）

- `python -m unittest discover -s tests/release-tests -v`：5 组回归全部通过（含多个 tag 与无效段落子用例）；真实 `V0.1.0` 提取成功，`V9.9.9` 退出码 1 且指出缺失版本。
- `actionlint 1.7.12` 工作流语法校验通过（未安装 ShellCheck/Pyflakes，关闭这两项外部检查）；`git diff --check` 通过，Dockerfile 与 compose.yaml 无改动。
- 远端 `V0.1.0` 推送及实际结果待记录。
- 本机无 Docker；容器启动、Compose 拉取与容器内分析/对话主链路未验证。本次发布任务不调用真实模型。

工作流语法与权限依据：[GitHub Actions 工作流语法](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)、[发布 Docker 镜像](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images)、[gh release create](https://cli.github.com/manual/gh_release_create)。
