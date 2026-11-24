# CinePersona 媒体服务器插件集合

[English](README.md) | [简体中文](README.zh-CN.md)

[CinePersona](https://cinepersona.com) 官方媒体服务器同步插件集合：电影与剧集播放完成度达到 80% 后，自动将观影记录与播放进度同步到 CinePersona；支持双向同步、设备码一键授权绑定以及网页端内嵌评分与短评弹窗。

## 实机截图

![媒体服务器网页端内嵌 CinePersona 10 星评分与短评弹窗](assets/screenshot-rating-modal.png)

| 电影详情页操作栏同步评分入口 | RFC 8628 设备码一键授权绑定 |
| :---: | :---: |
| ![电影详情页原生操作栏中的 CinePersona 评分按钮](assets/screenshot-detail-action-bar.png) | ![RFC 8628 设备码授权弹窗](assets/screenshot-device-pairing.png) |

## 包含组件

- `jellyfin/Jellyfin.Plugin.CinePersona`：Jellyfin 10.10+ 原生 C# 插件。
- `Emby.Plugin.CinePersona`：Emby Server 4.8.x 原生 C# 插件（分标准审核版与 Geek 网页增强版）。
- `plex/CinePersona.PlexRelay`：Plex Webhook 轻量转发服务（Go）。

## Emby 两个版本怎么选？（二选一，切勿同时安装）

| 版本 | 下载文件 | 适合人群 | 绑定方式 |
| :--- | :--- | :--- | :--- |
| **标准版（推荐多数人）** | [`Emby.Plugin.CinePersona.dll`](https://github.com/cinepersona/cinepersona-media-server-plugins/releases/download/v1.2.1/Emby.Plugin.CinePersona.dll) | 只需要后台自动同步电影/剧集进度与评分，不改动 Emby 网页文件，最稳定 | Emby 插件设置填 API Key |
| **网页打分版（Geek 进阶）** | [`Emby.Plugin.CinePersona.Geek.dll`](https://github.com/cinepersona/cinepersona-media-server-plugins/releases/download/v1.2.1/Emby.Plugin.CinePersona.Geek.dll) | 想在 Emby 电影详情页直接点「★ 评分 & 写短评」（如上方截图），会注入 `dashboard-ui/index.html` | 8 位设备码或 API Key |

## 安装方式

### Jellyfin
1. 进入 **Jellyfin 控制台 → 插件 → 存储库**，添加仓库地址：`https://cinepersona.com/jellyfin.json`（或从 [GitHub Releases](https://github.com/cinepersona/cinepersona-media-server-plugins/releases) 手动下载）。
2. 在目录中安装 **CinePersona** 并重启 Jellyfin。
3. 在插件设置或任意电影详情页点击「连接」完成设备码授权，或在 `https://cinepersona.com/settings/api-keys` 创建 API Key（勾选 `sync.write` 与 `library.read`）填入。

> **Docker 用户提示**：网页内嵌评分按钮会向 `jellyfin-web/index.html` 注入脚本；如果容器内 `jellyfin-web` 目录为只读，服务端观影与评分同步仍可正常工作，仅不显示网页内嵌按钮。

### Emby
1. 从 [GitHub Releases](https://github.com/cinepersona/cinepersona-media-server-plugins/releases) 下载 `Emby.Plugin.CinePersona.dll`（标准版）或 `Emby.Plugin.CinePersona.Geek.dll`（含网页评分弹窗版）。
2. 将 DLL 放入 Emby Server 的 `plugins` 目录并重启。
3. 在后台插件设置中配置 API Key（默认服务器地址 `https://cinepersona.com`）。
