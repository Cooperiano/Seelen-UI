# Seelen Desktop Suite — Julian's adaptation

基于 [Seelen UI 2.8.8](https://github.com/eythaann/Seelen-UI/tree/v2.8.8) 的 Windows 桌面套件改造，由 [Julian Cooper / Cooperiano](https://github.com/Cooperiano) 主导产品方向、交互设计、组件整合与实机反馈迭代，使用 Codex 辅助实现。

不是从零重写 Seelen，也不是 Apple 软件或驱动移植。我们复用 Seelen 的顶栏、Dock、快捷设置、主题和窗口基础设施，把系统监测与触控板组件接进统一桌面入口。

## 这次公开了什么

- **统一主设置窗口**：桌面套件、Stats、触控板设置共用左侧导航；调整后自动保存，显示保存状态与失败反馈。
- **顶栏整合**：读取 Stats v7 数据；显示输入法 Shift 中/英模式；托盘平铺与横向溢出访问，时间与快捷设置独立保留空间。
- **Dock 应用逻辑**：一次性迁移 Windows 固定应用顺序；固定区与运行区分隔；跨分隔线拖动固定/取消固定，保存后保留自定义顺序。
- **桌面生命周期**：统一组件启停、退出和当前用户自启动入口；Dock 常显/自动隐藏统一设置；原任务栏采用带心跳的隐藏与退出恢复机制。
- **回归代码**：输入模式、数据契约、布局、排序、拖放、自动保存、生命周期与任务栏策略测试。

本仓库公开 **Seelen 主程序改造、Stats 1.0.5 收集器及共享协议源码、接入代码与测试**。Stats 自研部分采用 AGPL，见 [components](components/README.md)。**触控板组件与总览辅助程序的完整改造源码**在独立的 [Touchpad Control fork](https://github.com/Cooperiano/ThreeFingerDragOnWindows) 中公开，保留上游 MIT。可选 Activity Monitor 及安装包不包含在本次发布中。缺少组件时相关能力不可用，不会虚构指标。详见[来源与贡献](documentation/SOURCES-AND-CONTRIBUTIONS.md)、[架构与构建](documentation/desktop-suite-fork.md)。

## 来源与许可

上游 Seelen UI 由其作者与贡献者开发，本 fork 保留上游历史、版权和 [GNU AGPL v3 许可文本](LICENSE)。主应用元数据声明 `AGPL-3.0-or-later`；部分上游库声明 `AGPL-3.0-only`，各自声明保持不变。不是 MIT 重新授权。第三方资源及依赖继续适用各自许可。

Touchpad Control 源于 Clément Grennerat 的 [ThreeFingerDragOnWindows](https://github.com/clementgre/ThreeFingerDragOnWindows)（MIT）；这次不把其二进制或未整理的修改版源码混入主程序。Apple/macOS 只作为用户体验参考，不声称采用了其非公开加速算法。

## 状态：实验性源码发布

这是持续迭代的 Windows 本机预览方案，不是稳定发行版，也不是上游官方发行。

- 用户已确认测试中的 Shift 中/英标识跟随、Win+D 顶栏保留和一次 Dock 触边展开/离开收起；不代表所有应用、全屏、多屏与长时使用均已验收。
- **已知问题**：原 Windows 任务栏偶尔出现，尚未捕获根因；Dock 长用后保持展开的反馈需持续复核。不能用单测替代真实桌面验收。
- 三指拖拽已能工作，但曲线手感仍需改进；没有承诺与 macOS 完全一致。
- 此次只发布源码与文档，不提供签名安装包；不要把上游下载链接当作套件安装器。

详见[构建说明](documentation/desktop-suite-fork.md)和[验证边界](documentation/VERIFICATION.md)。[上游原版说明](README.upstream.md)保留供参考，其中发行和社区链接属于上游，不代表本 fork 已获得支持或背书。

## English

Julian Cooper's experimental Windows desktop-suite adaptation of **Seelen UI 2.8.8**, not a shell built from scratch. It reuses the upstream toolbar, Dock, Quick Settings and window infrastructure, integrating optional Stats and Touchpad components through one settings and lifecycle surface. Julian leads product requirements, interaction direction, integration decisions and hands-on feedback; implementation is assisted by Codex.

This repository contains the adapted **host, Stats collector/shared protocol sources and regression tests**. The complete Touchpad/overview-helper adaptation is published in its [separate MIT fork](https://github.com/Cooperiano/ThreeFingerDragOnWindows). Optional Activity Monitor sources and a signed suite installer are not included. The owner chose AGPL for original Stats code; upstream and third-party terms remain intact. Apple/macOS is an interaction reference, not private code. See [provenance](documentation/SOURCES-AND-CONTRIBUTIONS.md), [build instructions](documentation/desktop-suite-fork.md) and [verification limits](documentation/VERIFICATION.md).
