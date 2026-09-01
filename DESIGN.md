---
version: alpha
name: "Eiri 发票报销助手"
description: "面向个人本地报销整理的克制型 Windows 工作台，以账册脊线和高密度清单建立识别。"
colors:
  primary: "#176B5B"
  primary-hover: "#125749"
  canvas: "#F3F6F5"
  surface: "#FFFFFF"
  surface-subtle: "#F7F9F8"
  border: "#DDE5E2"
  text-primary: "#17221F"
  text-secondary: "#63716D"
  focus: "#238671"
  danger: "#B4473B"
  danger-hover: "#F7DDD9"
typography:
  display:
    fontFamily: "Segoe UI Variable Display, Microsoft YaHei UI, sans-serif"
    fontSize: "1.75rem"
    lineHeight: "1.2"
  body:
    fontFamily: "Segoe UI Variable Text, Microsoft YaHei UI, sans-serif"
    fontSize: "0.875rem"
    lineHeight: "1.5"
  data:
    fontFamily: "Cascadia Mono, Microsoft YaHei UI, monospace"
rounded:
  DEFAULT: "0.375rem"
  sm: "0.25rem"
  md: "0.375rem"
  lg: "0.625rem"
spacing:
  control-gap: "0.5rem"
  panel-gap: "0.875rem"
  page-gutter: "1.5rem"
  section-gap: "1.25rem"
components:
  button: { height: "2.25rem" }
  input: { height: "2.25rem" }
  table: { height: "3rem" }
  panel: { rounded: "0.625rem" }
---

# Eiri 发票报销助手 Design System

## Overview

### Creative North Star

界面参考一本摊开的本地报销账册：左侧是可快速扫描的订单清单，右侧是当前条目的批注页。细窄的青绿色“账册脊线”只出现在工作区标题和关键状态处，成为主要识别元素。

### Product context and register

- **Audience and primary job：** 单人在 Windows 桌面上整理电商订单、电子发票和报销里程碑。
- **Target market(s) and evidence：** 当前产品文案与领域模型面向中文用户；依据 `CONTEXT.md` 和 `docs/architecture.md`。
- **Locale(s) and language policy：** 首版使用简体中文，领域词汇遵循 `CONTEXT.md`，不混入未解释的英文界面词。
- **Usage scene：** 本地文件密集操作，以桌面鼠标和键盘为主，需要同时扫描列表并编辑单个订单。
- **Register：** 产品型界面。任务清晰度、稳定布局和信息密度优先。
- **Memorable signature：** 4px 青绿色账册脊线，以及同色的当前选择和关键操作反馈。
- **Restraint：** 表格、表单和次要操作保持安静；不叠加装饰性插画、渐变或多层卡片。
- **Anti-references：** 避免营销式大数字仪表盘、玻璃拟态、过度圆润的胶囊按钮和大面积品牌色。
- **Token ownership/runtime mapping：** 本文件是视觉意图和规范值来源；`src/Eiri.Reimbursement.Desktop/App.xaml` 实现共享资源，`ThemeManager.cs` 提供浅色与深色映射。变更三者时需同步核对。

## Colors

浅色主题使用 `canvas` 承托白色工作面，`border` 划分层级。`primary` 仅用于主操作、焦点、选择和账册脊线。正文使用 `text-primary`，说明文字使用 `text-secondary`。危险操作使用 `danger` 并保持与安全主操作分离。深色主题保留同一语义层级，高对比模式交由系统颜色与原生 WPF 控件能力处理。

## Typography

标题使用 Segoe UI Variable Display，正文和控件使用 Segoe UI Variable Text；中文回退到 Microsoft YaHei UI。金额、日期、发票号等数据优先使用 Cascadia Mono，缺失时安全回退。主标题只使用 SemiBold，正文避免无意义加粗。

## Layout

窗口采用顶部应用栏、工作区标题、稳定状态条、主从双栏结构。订单表占据剩余宽度，订单详情固定为约 380px；未选择订单时详情栏折叠，列表自然扩展。页面边距 24px，面板间距 14px，控件间距 8px。表格拥有内部滚动，详情表单在自己的滚动区域中完整可达。

## Elevation & Depth

层级依靠底色和 1px 边线表达。静态面板不使用阴影；菜单、系统弹窗等浮层沿用 WPF 平台层级。深色主题通过表面明度差建立分层。

## Shapes

主面板使用 10px 圆角，普通控件使用 6px，小型状态与内部条目使用 4px。按钮保持矩形轮廓，不使用胶囊形。分隔线为 1px，账册脊线是唯一较粗的结构线。

## Components

### Foundational visual states

可交互控件具有明确的悬停、按下、焦点和禁用状态。焦点使用 `focus` 色 2px 轮廓。选中行使用低饱和青绿色底，文字保持高对比。加载时保持控件尺寸不变；当前实现未引入骨架屏。

### Buttons and actions

主按钮为实心 `primary`，普通操作为描边表面按钮，低优先级操作使用透明按钮。危险操作使用 `danger` 文本和浅色危险底，只在确认步骤提高强调。常规高度为 36px。

### Navigation and data display

顶栏只承载应用身份和低频菜单。订单清单是主导航面，行高 48px，表头和单元格内容统一居中；商家列使用只读文本，多商家以“第一个商家等”收束。详情选项卡使用底边指示当前页，不做独立卡片。金额、日期和发票号保持易扫描。

### Forms and overlays

字段标签位于输入框上方，输入控件高度一致。文件投放区提供点击按钮这一等价路径。删除继续使用当前 WPF 所有者窗口确认框，并默认选择安全选项；后续如引入共享对话框系统，应集中迁移。

### Iconography

当前不引入第三方图标集。操作以清晰中文标签为主，避免含义不明的图标按钮。

### Motion

不使用装饰性动效。状态切换依赖即时颜色和可见性反馈；若未来加入动效，时长应短于 180ms，并尊重系统减少动画设置。

### Content and data visualization

文案直接描述动作与结果，领域术语遵循 `CONTEXT.md`。金额使用人民币两位小数，日期使用 `yyyy-MM-dd`，状态反馈保留具体对象数量。

## Do's and Don'ts

- **Do：** 用清单、细线和留白建立秩序，让用户快速比较订单。
- **Do：** 在浅色和深色主题中保持相同的语义颜色层级和控件尺寸。
- **Don't：** 为每个信息组再套一层阴影卡片，制造无效层级。
- **Don't：** 用大面积高饱和颜色、渐变或装饰图形干扰金额与里程碑判断。
