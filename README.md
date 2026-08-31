# DiaryOut

## 纯vibe coding产物，请注意。

从「你的日记」（nideriji.cn）批量导出日记与图片的 Windows 桌面工具（.NET 8 + WPF）。

## 主要特性

- **安全凭据**：Token 仅存于内存，绝不落盘。
- **导出范围**：支持全部导出、按日期范围筛选或手动勾选。
- **多格式支持**：
  - **HTML**：内置独立简洁样式，支持离线浏览。
  - **Markdown**：标准 Markdown 格式，图片相对路径引用。
  - **PDF**：支持单篇日记 PDF 与全量合并 PDF（图片内嵌，中文字体优先微软雅黑）。
- **图片处理**：原图自动下载并关联对应日记。
- **断点续传 & 增量导出**：基于内容哈希去重，已导出且未修改的日记自动跳过。
- **稳定性**：自带请求限速、超时与指数退避重试。

## 站点 API（2026-08-31 实测）

| 用途 | 请求 |
|---|---|
| 登录 | `POST /api/login/`，multipart 字段 `email`、`password`；返回 `{error, token, userid, user_config}`；业务错误以 HTTP 403 + `{"error": n}` 返回 |
| 全量同步 | `POST /api/v2/sync/`，multipart 字段 `user_config_ts/diaries_ts/readmark_ts/images_ts`（0=全量） |
| 图片下载 | `GET https://f.nideriji.cn/api/image/{user_id}/{image_id}/` |
| 鉴权 | 请求头 `auth: token <JWT>` |

正文格式：按行解析；`[HH:mm]` 或 `[HH:mm:ss]` 为时间分段；`[图<image_id>]` 为图片占位符，与 sync 返回的
`images` 元数据（`image_id/width/height/lt`）按 id 匹配。

> 注意：`images[].width/height` 服务端可能返回浮点数（如 `820.0`）

## 输出目录结构

```text
output/
  index.json          # 日记索引元数据
  failures.json       # 导出失败清单（若有）
  state.json          # 断点与内容哈希状态
  html/               # HTML 格式日记及图片
  markdown/           # Markdown 格式日记及图片
  pdf/                # 单篇 PDF 及合并 PDF
```

## 项目结构

- `src/DiaryOut.Core/`：核心库（API 通信、数据解析、导出逻辑与断点状态）。
- `src/DiaryOut.App/`：WPF 桌面客户端。
- `tools/DiaryOut.Smoke/`：命令行冒烟测试工具。

## 已知限制

- 天气/心情等元数据按 sync 实际返回字段导出，缺失时跳过
- 登录遇验证码/风控时需稍后再试（站点对登录接口有频率限制，错误码 3）