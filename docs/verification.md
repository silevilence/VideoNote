# 本轮验证记录

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
