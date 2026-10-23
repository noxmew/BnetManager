<img src="src/BnetManager/Assets/mark.svg" width="64" height="64" alt="BnetManager">

# BnetManager

Windows 战网多账号切换工具，保存各账号的本地登录状态，切换后自动启动官方战网。

![主界面，账号名称为演示信息](docs/images/main-window.png)

## 功能

- 保存、启动和切换多个账号。
- 提供国服、亚服、美服和欧服的登录入口。
- 编辑账号备注、更新登录状态、移除本地记录。
- 加密保存登录快照，操作失败或中断时尝试自动恢复。

## 使用

运行环境为 Windows 11，目前已在 x64 上验证。需先安装并启动一次官方战网；程序未找到安装位置时，在右上角设置中选择 `Battle.net.exe`。

1. 在官方战网登录账号，勾选「保持登录」，再点击「保存当前账号」。
2. 添加其他账号时，先保存当前账号，再点击「登录新账号」选择区服；完成登录后保存。
3. 点击账号右侧的「启动」或「切换」即可使用该账号。
4. 「…」菜单可以更新登录状态、修改备注或移除本地记录。

保存和切换前请先退出游戏。程序会自动暂时退出战网，操作完成后按需重新打开。四区服均有登录配置，尚未完成所有区服的完整登录实测；登录状态失效时，需要在官方战网重新登录。

数据位于 `%LOCALAPPDATA%\BnetManager`。`accounts.json` 为明文账号目录，`.session` 为加密登录快照；请妥善保管，不要公开上传。快照供原电脑、原 Windows 用户及原战网安装目录使用。

如果提示操作中断，退出游戏和战网后重新打开 BnetManager，让程序完成自动恢复；不要删除 `pending.session`。

## 构建

使用 C#、WPF 和 .NET 10 SDK：

```powershell
dotnet build src/BnetManager/BnetManager.csproj -c Release
```

仓库仅有一个 GitHub Actions 工作流：推送到 `main`、提交 PR 或手动触发时，自动生成 Windows x64 和 ARM64 自包含压缩包，可在对应运行的 Artifacts 中下载，无需单独安装 .NET。ARM64 尚未完成实机验证。

## 许可

MIT。自包含压缩包会自动附带依赖的许可文件。

<details>
<summary>MIT License</summary>

<!-- MIT-LICENSE-BEGIN -->
MIT License

Copyright (c) 2026 BnetManager contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
<!-- MIT-LICENSE-END -->

</details>
