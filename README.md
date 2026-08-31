# DiaryOut

从「你的日记」（nideriji.cn）自动导出日记（含图片）的 Windows 桌面工具，.NET 8 + WPF。
仅用于本人账号的个人备份，默认限速请求。

## 结构

```
src/DiaryOut.Core/   核心库：API 客户端、内容解析、导出（HTML/Markdown/PDF）、断点状态
src/DiaryOut.App/    WPF 桌面程序
tools/DiaryOut.Smoke/ 冒烟测试控制台（凭据仅来自命令行参数，不落盘；未加入解决方案）
```

## 站点 API（2026-08-31 实测）

| 用途 | 请求 |
|---|---|
| 登录 | `POST /api/login/`，multipart 字段 `email`、`password`；返回 `{error, token, userid, user_config}`；业务错误以 HTTP 403 + `{"error": n}` 返回 |
| 全量同步 | `POST /api/v2/sync/`，multipart 字段 `user_config_ts/diaries_ts/readmark_ts/images_ts`（0=全量） |
| 图片下载 | `GET https://f.nideriji.cn/api/image/{user_id}/{image_id}/` |
| 鉴权 | 请求头 `auth: token <JWT>` |

正文格式：按行解析；`[HH:mm]` 为时间分段；`[图<image_id>]` 为图片占位符，与 sync 返回的
`images` 元数据（`image_id/width/height`）按 id 匹配。

## 功能（对应需求基线）

- 程序内登录；仅保存 token（DPAPI 加密，`%LocalAppData%\DiaryOut\session.dat`），不保存密码
- 导出范围：全部 / 日期范围 / 关键词 / 手动勾选
- 导出格式：HTML（独立简洁样式，离线可看）、Markdown、单篇 PDF、合并 PDF（QuestPDF，中文字体优先微软雅黑）
- 图片：原图下载到每篇的 `images/` 目录；失败显示占位文字并记入失败清单
- 断点续传与去重：`state.json` 记录内容哈希，未变化跳过，变化重导；合并 PDF 会纳入未变化的日记
- 限速（默认 500ms 间隔）、超时 30s、指数退避重试（默认 3 次）
- 输出结构：

```
输出目录/
  index.json      索引
  failures.json   失败清单
  state.json      断点/去重状态
  日记合集.pdf    合并 PDF（可选）
  2026-08-31-标题/
    diary.html / diary.md / diary.pdf
    images/123.jpg
```

## 运行

```powershell
dotnet run --project src/DiaryOut.App
```

冒烟测试：

```powershell
dotnet run --project tools/DiaryOut.Smoke -- <email> <password> <outputDir>
```

## 已知限制

- 测试账号暂无含图片日记，图片下载链路已按前端 JS 行为实现，待真实图片数据验证
- 天气/心情等元数据按 sync 实际返回字段导出，缺失时跳过
- 登录遇验证码/风控时需稍后再试（站点对登录接口有频率限制，错误码 3）
