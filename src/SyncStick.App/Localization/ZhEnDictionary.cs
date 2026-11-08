namespace SyncStick.App.Localization;

/// <summary>
/// Chinese → English copy used by the desktop UI when the app runs in English.
/// Static strings are translated through this exact-match table; values built at
/// runtime (counts, file names, error messages) go through <see cref="TranslationRules"/>
/// or are localized explicitly at the call site.  English headings intentionally
/// carry no trailing full stop.
/// </summary>
internal static class ZhEnDictionary
{
    internal static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        // ---------------------------------------------------------- window chrome
        ["SyncStick · U 盘自动同步"] = "SyncStick · USB auto-sync",
        ["U 盘自动同步"] = "USB auto-sync",
        ["最小化"] = "Minimize",
        ["最大化"] = "Maximize",
        ["关闭"] = "Close",
        ["WORKSPACE / 工作区"] = "WORKSPACE",
        ["自动同步"] = "Auto-sync",
        ["插入目标 U 盘后自动开始同步"] = "Start syncing automatically when the target USB drive is plugged in",
        ["关闭后，仍可在总览手动同步。"] = "You can still sync manually from Overview after closing.",
        ["本机运行 · 不上传文件"] = "Runs locally · No uploads",

        // ---------------------------------------------------------- dashboard
        ["SYNC DESK / 同步总览"] = "SYNC DESK",
        ["插上 U 盘，项目就位"] = "Plug in, and your files are ready",
        ["选定要跟随的内容和一块外部存储设备。之后每次插入，SyncStick 都会把有变化的文件覆盖进去。"] =
            "Choose the content to follow and an external drive. From then on, SyncStick overwrites changed files every time it is plugged in.",
        ["管理内容"] = "Manage content",
        ["同步路径"] = "Sync path",
        ["编辑内容"] = "Edit content",
        ["01 / 来源"] = "01 / SOURCES",
        ["02 / 规则"] = "02 / RULES",
        ["03 / 目标"] = "03 / TARGET",
        ["最近一次同步"] = "Last sync",
        ["全部记录"] = "All records",
        ["还没有同步过"] = "No sync yet",
        ["第一次写入后，这里会留下文件数量、体积和用时。"] =
            "After the first write, file count, size and duration will appear here.",
        ["文件"] = "FILES",
        ["体积"] = "SIZE",
        ["用时"] = "TIME",
        ["最近活动"] = "Recent activity",
        ["全部"] = "All",
        ["暂无活动"] = "No activity yet",
        ["插上 U 盘或手动同步一次，记录会出现在这里。"] =
            "Plug in the USB drive or run a manual sync and it will show up here.",

        // ------------------------------------------------------------- sources
        ["SOURCES / 同步内容"] = "SOURCES",
        ["决定 U 盘里放什么"] = "Decide what goes on the USB drive",
        ["可以添加多个文件或文件夹。文件夹会保留原目录结构；U 盘上的同名文件会被新版本覆盖。"] =
            "Add multiple files or folders. Folders keep their original structure; files with the same name on the USB drive are overwritten by newer versions.",
        ["添加文件"] = "Add files",
        ["添加文件夹"] = "Add folder",
        ["只写入你选中的内容。默认保留目录结构，并覆盖有变化的同名文件；不会删除 U 盘上的其他文件。"] =
            "Only what you select gets written. Folder structure is kept and changed files are overwritten; other files on the USB drive are never deleted.",
        ["还没有同步内容"] = "No sync content yet",
        ["先选一个项目文件夹，或几个常用文件。选中后就会出现在这里。"] =
            "Start with a project folder, or a few frequently used files. They will show up here once selected.",
        ["同步项"] = "Sync items",
        ["拖入文件也可以添加"] = "Drag files here",
        ["启用或暂停这个同步项"] = "Enable or pause this sync item",
        ["启用或暂停这一项"] = "Enable or pause this item",
        ["路径不存在"] = "Path does not exist",
        ["U 盘内位置"] = "Location on USB",
        ["留空表示使用默认位置。文件夹默认以自己的名字命名；文件默认放在 U 盘根目录。也可以点右侧文件夹按钮直接在 U 盘上选择。"] =
            "Leave empty to use the default. Folders default to their own name; files default to the USB root. You can also click the folder button on the right to choose directly on the USB drive.",
        ["选择 U 盘内的写入文件夹"] = "Choose the write folder on the USB drive",
        ["在 U 盘上选择写入文件夹"] = "Choose a write folder on the USB drive",
        ["在资源管理器中打开"] = "Open in File Explorer",
        ["从同步内容中移除"] = "Remove from sync content",
        ["继续添加"] = "Add more",
        ["也可以直接把文件或文件夹拖进这个窗口。"] =
            "You can also drag files or folders straight into this window.",
        ["提示：每一行的“U 盘内位置”是相对 U 盘根目录的路径，点右侧文件夹按钮可以直接在 U 盘上选择。留空时，文件夹以自己的名字命名，文件直接放在根目录。"] =
            "Tip: “Location on USB” on each row is a path relative to the USB root. Click the folder button on the right to choose directly on the drive. When empty, folders use their own name and files go to the root.",
        ["松开即可添加"] = "Drop to add",
        ["文件与文件夹都可以"] = "Files and folders both work",
        ["文件或文件夹"] = "Files or folders",
        ["U 盘根目录"] = "USB root",
        ["写入到"] = "Write to",
        ["目标子目录无效"] = "Invalid destination subfolder",
        ["文件夹 · 保留原目录结构"] = "Folder · Original structure kept",
        ["单文件"] = "Single file",

        // ------------------------------------------------------------- devices
        ["TARGET / 目标设备"] = "TARGET",
        ["选一块 U 盘作为落点"] = "Pick a USB drive as the destination",
        ["SyncStick 会记住这块设备，并在它插入时自动同步。换一个 USB 口、换一个盘符，都不会影响识别。"] =
            "SyncStick remembers this device and syncs automatically when it is plugged in. Changing the USB port or drive letter does not affect recognition.",
        ["刷新设备"] = "Refresh devices",
        ["容量"] = "CAPACITY",
        ["可用"] = "FREE",
        ["格式"] = "FORMAT",
        ["移除目标"] = "Remove target",
        ["还没有选择目标设备"] = "No target device selected",
        ["尚未选择目标设备"] = "No target device selected",
        ["在下方选择一块 U 盘。之后每次插入，SyncStick 都会自动写入。"] =
            "Pick a USB drive below. SyncStick will write to it automatically every time it is plugged in.",
        ["可移动设备"] = "Removable devices",
        ["没有发现可移动设备"] = "No removable devices found",
        ["插入 U 盘后会自动出现在这里。如果它是 USB 硬盘，可能被识别为固定磁盘。"] =
            "Plug in a USB drive and it will show up here. USB hard drives may be detected as fixed disks.",
        ["重新扫描"] = "Scan again",
        ["设备容量"] = "DEVICE CAPACITY",
        ["已用 "] = "Used ",
        ["显示其他驱动器"] = "Show other drives",
        ["USB 移动硬盘和固态硬盘可能被 Windows 标记为固定磁盘。仅在确认安全时使用。"] =
            "USB hard drives and SSDs may be marked as fixed disks by Windows. Use only when you are sure it is safe.",
        ["FIXED / 其他驱动器"] = "OTHER DRIVES",
        ["固定磁盘"] = "Fixed disk",
        ["SyncStick 通过卷序列号记住设备，不需要管理员权限，也不会读取 U 盘内容清单以外的信息。格式化 U 盘会改变序列号，需要重新选择。"] =
            "SyncStick remembers devices by volume serial number, needs no administrator rights, and reads nothing beyond the drive's file list. Formatting the drive changes its serial number, so you will need to select it again.",
        ["可移动磁盘"] = "Removable disk",
        ["未知格式"] = "Unknown format",
        ["本地"] = "Local",
        ["当前目标"] = "Current target",
        ["设为目标"] = "Set as target",

        // ------------------------------------------------------------ settings
        ["SETTINGS / 设置"] = "SETTINGS",
        ["调成你的节奏"] = "Set your own pace",
        ["同步规则、启动方式与通知都在这里。所有设置只保存在本机。"] =
            "Sync rules, startup behavior and notifications all live here. Settings are stored on this device only.",
        ["同步行为"] = "Sync behavior",
        ["智能覆盖"] = "Smart overwrite",
        ["只复制新增或已变化的文件；同名的旧文件会被覆盖。关闭后会每次全部重写。"] =
            "Copy only new or changed files; older files with the same name are overwritten. When off, everything is rewritten every time.",
        ["复制时间戳"] = "Copy timestamps",
        ["保留来源文件的修改时间，方便在 U 盘上识别版本。"] =
            "Keep the source file's modified time so versions are easy to spot on the USB drive.",
        ["忽略系统临时文件"] = "Ignore system temp files",
        ["跳过 Thumbs.db、desktop.ini、~$ 临时文档等系统文件。"] =
            "Skip system files such as Thumbs.db, desktop.ini and ~$ temporary documents.",
        ["镜像清理"] = "Mirror cleanup",
        ["成功同步后，删除所选文件夹在 U 盘内对应目录中的多余文件。请确认每一项的“U 盘内位置”。"] =
            "After a successful sync, delete extra files in the matching folder on the USB drive. Check each item's “location on USB” first.",
        ["数据与隐私"] = "Data & privacy",
        ["数据目录"] = "Data folder",
        ["打开数据目录"] = "Open data folder",
        ["清空活动记录"] = "Clear activity",
        ["SyncStick 不联网、不上传文件。配置与活动记录仅保存在本机。移除目标设备不会修改 U 盘内容。"] =
            "SyncStick never goes online or uploads files. Settings and activity stay on this device. Removing the target device does not change the USB drive.",
        ["自动与启动"] = "Automation & startup",
        ["插入后自动同步"] = "Sync on insert",
        ["检测到目标设备接入后，等待片刻再开始写入。"] =
            "Wait a moment after the target device is detected, then start writing.",
        ["启动时同步已连接的设备"] = "Sync connected device at launch",
        ["如果打开应用时目标 U 盘已经插着，也自动同步一次。"] =
            "If the target USB drive is already plugged in when the app opens, sync once automatically.",
        ["开机自动启动"] = "Start with Windows",
        ["登录 Windows 后在托盘静默启动。优先写入当前用户启动项，被拦截时改用启动文件夹快捷方式；图标若没出现，点右下角 ^ 展开。"] =
            "Start quietly in the tray after you sign in to Windows. SyncStick writes the current user's startup entry first and falls back to a Startup-folder shortcut if blocked. If the icon is missing, click ^ in the lower-right corner.",
        ["关闭窗口时最小化到托盘"] = "Minimize to tray on close",
        ["关闭主窗口后继续在后台监听；从托盘菜单可以彻底退出。Windows 11 可能会把新图标收进任务栏右下角的 ^ 里。"] =
            "Keep listening in the background after the window closes; exit completely from the tray menu. Windows 11 may hide new icons under ^ in the lower-right corner.",
        ["完成时发送通知"] = "Notify when finished",
        ["同步结束后显示桌面通知；失败时仍会保留活动记录。"] =
            "Show a desktop notification when a sync ends; failed runs are still recorded in activity.",
        ["把需要的项目，随 U 盘一起带走。"] = "Take the projects you need with your USB drive.",
        ["Windows 10 / 11 · WPF / .NET 10 单文件应用\n本机运行 · 不上传文件 · 不需要管理员权限"] =
            "Windows 10 / 11 · WPF / .NET 10 single-file app\nRuns locally · No uploads · No admin required",
        ["界面语言"] = "Interface language",
        ["跟随系统"] = "Follow system",
        ["重启 SyncStick 后生效。"] = "Restart SyncStick to apply.",

        // ------------------------------------------------------------- activity
        ["ACTIVITY / 活动记录"] = "ACTIVITY",
        ["每一次写入，都有迹可循"] = "Every write leaves a trail",
        ["这里保留最近 60 次同步的结果、文件数量、体积和用时。记录只存在本机。"] =
            "The last 60 sync runs are kept here with file counts, sizes and durations. Records stay on this device.",
        ["清空记录"] = "Clear records",
        ["SAVED RUNS / 记录数"] = "SAVED RUNS",
        ["SUCCESS RATE / 成功率"] = "SUCCESS RATE",
        ["TOTAL WRITTEN / 累计写入"] = "TOTAL WRITTEN",
        ["同步历史"] = "Sync history",
        ["还没有同步记录"] = "No sync history yet",
        ["插上目标 U 盘或手动同步一次，结果会保存在这里。"] =
            "Plug in the target USB drive or run a manual sync, and the result will be saved here.",
        ["记录保存在本机的应用数据目录中，最多保留 60 条。清空记录不会删除 U 盘上的文件。"] =
            "Records are stored in this device's app data folder, up to 60 entries. Clearing records never deletes files on the USB drive.",

        // -------------------------------------------------------------- dialogs
        ["确认操作"] = "Confirm action",
        ["取消"] = "Cancel",
        ["确定"] = "OK",
        ["知道了"] = "Got it",
        ["同步还在进行"] = "Sync in progress",
        ["现在退出会中断正在写入的文件。未完成的临时文件会被清理，但 U 盘可能留下不完整的新版本。\n\n确定要退出吗？"] =
            "Exiting now will interrupt files being written. Incomplete temporary files are cleaned up, but the USB drive may keep an incomplete new version.\n\nExit anyway?",
        ["仍然退出"] = "Exit anyway",
        ["开启镜像清理？"] = "Enable mirror cleanup?",
        ["每次成功同步后，SyncStick 会删除所选文件夹在 U 盘内对应目录中“多余”的文件。\n\n其他目录不会被触碰，但如果“写入到”填错，仍可能误删文件。建议先检查每个同步项的目标子目录。"] =
            "After each successful sync, SyncStick deletes “extra” files inside the matching folders on the USB drive.\n\nOther folders are untouched, but a wrong “write to” value can still delete files by mistake. Review each item's destination subfolder first.",
        ["开启镜像清理"] = "Enable mirror cleanup",
        ["选择固定磁盘？"] = "Choose a fixed disk?",
        ["仍然选择"] = "Choose anyway",
        ["清空活动记录？"] = "Clear activity?",
        ["这只会删除本机保存的同步历史，不会影响 U 盘或电脑上的任何文件。"] =
            "This only deletes sync history stored on this device. No files on the USB drive or computer are affected.",
        ["清空记录"] = "Clear records",
        ["先连接目标 U 盘"] = "Connect the target USB drive first",
        ["要直接选择 U 盘上的文件夹，需要先把目标 U 盘插上。\n\n也可以现在直接输入相对路径，例如：我的项目\\资料。插上 U 盘后会自动按这个路径写入。"] =
            "To choose a folder directly on the USB drive, plug the target drive in first.\n\nYou can also type a relative path now, for example: MyProject\\Data. SyncStick will write there once the drive is plugged in.",
        ["只能选择目标 U 盘里的文件夹"] = "Only folders on the target USB drive can be chosen",
        ["刚才选中的文件夹不在这块目标 U 盘上。\n\n请选择 U 盘里的文件夹；如果还没有合适的文件夹，可以在选择窗口里新建一个。"] =
            "The folder you selected is not on the target USB drive.\n\nChoose a folder on the drive; if none fits yet, create one in the picker window.",
        ["开机启动没有设置成功"] = "Could not enable start with Windows",

        // ---------------------------------------------------------------- toasts
        ["1 个同步项"] = "1 sync item",
        ["已添加 1 个同步项。"] = "Added 1 sync item.",
        ["最近 1 次同步"] = "Last run",
        ["写入 1 个文件"] = "Wrote 1 file",
        ["1 个文件"] = "1 file",
        ["跳过 1 个未变化的文件"] = "Skipped 1 unchanged file",
        ["清理 1 项"] = "1 item removed",
        ["同步失败："] = "Sync failed: ",
        ["操作失败："] = "Operation failed: ",
        ["已从同步内容中移除，U 盘上的文件不会被删除。"] =
            "Removed from sync content. Files on the USB drive are not deleted.",
        ["这块设备没有可读取的卷序列号，将按卷标和容量识别。"] =
            "This device has no readable volume serial number; it will be matched by label and capacity.",
        ["已移除目标设备，U 盘上的文件不会被修改。"] =
            "Target device removed. Files on the USB drive are untouched.",
        ["不能把目标 U 盘内部的文件作为同步来源。"] =
            "Files inside the target USB drive cannot be used as sync sources.",
        ["没有添加新内容：路径不存在、重复，或位于目标 U 盘内。"] =
            "Nothing was added: the path does not exist, is a duplicate, or is inside the target USB drive.",
        ["设备列表已刷新。"] = "Device list refreshed.",
        ["正在停止同步…"] = "Stopping sync…",
        ["活动记录已清空。"] = "Activity cleared.",
        ["镜像清理已开启，请确认目标子目录。"] =
            "Mirror cleanup is on. Check each destination subfolder.",
        ["镜像清理已关闭。"] = "Mirror cleanup is off.",
        ["已加入 Windows 开机启动。"] = "Added to Windows startup.",
        ["已取消开机启动。"] = "Removed from Windows startup.",
        ["开机启动没有设置成功。"] = "Could not enable start with Windows.",
        ["自动同步已开启。"] = "Auto-sync turned on.",
        ["自动同步已暂停，仍可手动同步。"] = "Auto-sync paused. You can still sync manually.",
        ["语言已保存，重启 SyncStick 后生效。"] = "Language saved. Restart SyncStick to apply.",
        ["语言已保存，重启后生效。"] = "Language saved. Restart to apply.",

        // ------------------------------------------------------- dashboard state
        ["同步总览"] = "Overview",
        ["同步内容"] = "Sources",
        ["目标设备"] = "Devices",
        ["活动记录"] = "Activity",
        ["设置"] = "Settings",
        ["目标 U 盘"] = "target USB drive",
        ["U 盘"] = "USB drive",
        ["未连接"] = "Not connected",
        ["正在扫描来源，准备同步计划…"] = "Scanning sources and preparing the plan…",
        ["准备写入…"] = "Preparing to write…",
        ["准备中"] = "Preparing",
        ["计算体积中"] = "Calculating size",
        ["扫描中"] = "Scanning",
        ["写入中"] = "Writing",
        ["清理中"] = "Cleaning up",
        ["已完成"] = "Completed",
        ["同步中"] = "Syncing",
        ["待机"] = "Idle",
        ["需检查"] = "Needs attention",
        ["已连接"] = "Connected",
        ["等待插入"] = "Waiting for device",
        ["设备需要确认"] = "Device needs confirmation",
        ["先选一块 U 盘"] = "Pick a USB drive first",
        ["等待 U 盘插入"] = "Waiting for the USB drive",
        ["还差同步内容"] = "Sync content is missing",
        ["这次已经写好了"] = "This one is ready",
        ["同时检测到多块相似的设备，无法确定该写哪一块。请到“目标设备”重新选择。"] =
            "Multiple similar devices were detected and the target is ambiguous. Reselect one in “Devices”.",
        ["指定要跟随的项目和一块外部存储设备。之后每次插入，SyncStick 都会自动把变化写进去。"] =
            "Choose the projects to follow and an external drive. After that, SyncStick writes changes automatically every time it is plugged in.",
        ["自动同步已暂停。插入 U 盘后，可以在总览中手动开始同步。"] =
            "Auto-sync is paused. After plugging in the USB drive, you can start a sync manually from Overview.",
        ["U 盘连接正常。先添加文件或文件夹，SyncStick 才知道要写什么。"] =
            "The USB drive is connected. Add files or folders so SyncStick knows what to write.",
        ["文件已是最新。"] = "Files are already up to date.",
        ["没有文件需要更新。"] = "Nothing needs updating.",
        ["自动同步已暂停，点击“立即同步”即可写入。"] =
            "Auto-sync is paused. Click “Sync now” to write.",
        ["需要确认"] = "Needs confirmation",
        ["尚未设置"] = "Not set up",
        ["等待内容"] = "Waiting for content",
        ["取消同步"] = "Cancel sync",
        ["选择 U 盘"] = "Choose USB drive",
        ["查看设备"] = "View devices",
        ["添加内容"] = "Add content",
        ["全部覆盖"] = "Full overwrite",
        ["会删除目标中多余的文件"] = "Deletes extra files on the target",
        ["保留目录结构 · 不删其他文件"] = "Keeps structure · Deletes nothing else",
        ["还没有选择内容"] = "No content selected",
        ["还没有选择设备"] = "No device selected",
        ["插入后自动识别"] = "Recognized when plugged in",
        ["已是最新"] = "Already up to date",
        ["写入完成"] = "Write complete",
        ["同步已取消"] = "Sync cancelled",
        ["同步未完成"] = "Sync incomplete",
        ["第一次同步后，这里会留下文件数量、体积和用时。"] =
            "After the first sync, file count, size and duration will appear here.",
        ["目标上的文件已经是新的。"] = "Files on the target are already current.",
        ["写入被中断，未完成的临时文件已清理。"] =
            "The write was interrupted; incomplete temporary files were cleaned up.",
        ["有文件未能写入，请查看活动记录。"] =
            "Some files could not be written. Check the activity log.",
        ["自动同步已开启"] = "Auto-sync on",
        ["自动同步已暂停"] = "Auto-sync paused",
        ["插入目标 U 盘后，自动比较并写入有变化的文件。"] =
            "Compare and write changed files automatically when the target USB drive is plugged in.",
        ["插入 U 盘后不会自动开始，可在总览中手动同步。"] =
            "Plugging in the USB drive does not start a sync automatically; start one manually from Overview.",
        ["还没有启用任何同步项。"] = "No sync items enabled yet.",
        ["发现相似设备"] = "Similar devices found",
        ["未设置"] = "Not set",
        ["无序列号（按卷标和容量识别）"] = "No serial number (matched by label and capacity)",

        // ------------------------------------------------------- activity rows
        ["昨天"] = "Yesterday",
        ["完成"] = "Completed",
        ["已取消"] = "Cancelled",
        ["未完成"] = "Incomplete",
        ["已是最新，无需写入"] = "Already up to date",
        ["同步被中断"] = "Sync was interrupted",
        ["有文件未能写入"] = "Some files could not be written",
        ["手动同步"] = "Manual",
        ["启动时同步"] = "At launch",
        ["插入 U 盘"] = "USB inserted",

        // -------------------------------------------------------- tray / system
        ["打开 SyncStick"] = "Open SyncStick",
        ["立即同步"] = "Sync now",
        ["插入 U 盘后自动同步"] = "Sync automatically when a USB drive is plugged in",
        ["退出"] = "Exit",
        ["SyncStick 启动失败。"] = "SyncStick failed to start.",
        ["SyncStick 遇到一个未处理的界面错误，但会继续运行。"] = "SyncStick hit an unhandled UI error but will keep running.",
        ["系统托盘不可用，已打开主窗口。你仍然可以正常同步，但请保持窗口打开。"] =
            "The system tray is unavailable, so the main window was opened. Syncing still works, but please keep the window open.",
        ["SyncStick 仍在后台监听 U 盘。图标可能在任务栏右下角的 ^ 里；右键可退出。"] =
            "SyncStick is still watching for USB drives in the background. The icon may be under ^ in the lower-right corner; right-click to exit.",
        ["Windows 任务栏还没有准备好，已打开主窗口。请保持窗口打开；托盘图标出现后可以关闭窗口。"] =
            "The Windows taskbar is not ready yet, so the main window was opened. Keep the window open; you can close it once the tray icon appears.",
        ["SyncStick 已在后台运行。如果看不到图标，请点任务栏右下角的 ^ 展开隐藏图标。"] =
            "SyncStick is running in the background. If you cannot see the icon, click ^ in the lower-right corner to show hidden icons.",
        ["系统托盘初始化失败，已打开主窗口。你仍然可以正常同步，但请保持窗口打开。"] =
            "System tray initialization failed, so the main window was opened. Syncing still works, but please keep the window open.",
        ["正在同步，请稍候。"] = "Syncing, please wait.",
        ["目标 U 盘还未连接。"] = "The target USB drive is not connected yet.",
        ["还没有选择要同步的内容。"] = "No content to sync has been selected yet.",
        ["SyncStick · 正在同步"] = "SyncStick · Syncing",
        ["SyncStick · 等待目标 U 盘"] = "SyncStick · Waiting for target USB drive",
        ["SyncStick · 尚未设置目标"] = "SyncStick · No target configured",
        ["SyncStick · 需要注意"] = "SyncStick · Needs attention",

        // ---------------------------------------------------------- file pickers
        ["选择要同步的文件夹"] = "Choose folders to sync",
        ["选择要同步的文件"] = "Choose files to sync",
        ["选择 U 盘上的写入文件夹"] = "Choose a write folder on the USB drive",

        // ---------------------------------------------------------------- rules
        ["规则"] = "Rules",
        ["RULES / 规则"] = "RULES",
        ["RULE / 规则"] = "RULE",
        ["为每块 U 盘准备一套规则"] = "A rule set for every USB drive",
        ["提前写好要同步的文件和它们的目标位置。插入 U 盘后，可以直接选择要用哪一套规则。"] =
            "Write down what to sync and where it goes. After a drive is inserted, choose the rule set you need.",
        ["编辑当前规则"] = "Edit active rule",
        ["新建规则"] = "New rule",
        ["每个规则都有自己的同步内容列表。切换规则只改变下一次写什么，不会移动或删除任何文件。"] =
            "Each rule has its own sync items. Switching rules only changes what gets written next; no files are moved or deleted.",
        ["规则列表"] = "Rule sets",
        ["输入新的规则名称，按保存后生效。"] = "Enter a new rule name, then save.",
        ["使用此规则"] = "Use this rule",
        ["编辑内容"] = "Edit items",
        ["重命名"] = "Rename",
        ["复制"] = "Duplicate",
        ["删除"] = "Delete",
        ["保存"] = "Save",
        ["只有一个规则时，插入目标 U 盘会自动同步。有多个规则时，可以在“设置”里选择插入时是否询问。也可以随时在“目标设备”页手动同步当前规则。"] =
            "With one rule, the target USB drive syncs automatically. With several rules, choose in Settings whether to ask on insert. You can also sync the active rule manually from Devices at any time.",
        ["这一页编辑的是当前规则的同步内容。"] = "This page edits the sync items of the active rule.",
        ["管理规则"] = "Manage rules",
        ["插入时选择规则"] = "Ask which rule on insert",
        ["有多个规则时，插入目标 U 盘后先选择要同步哪一套；关掉后自动使用这块设备上次用过的规则。"] =
            "With several rules, choose which one to sync after the target drive is inserted. When off, the rule last used with this drive is used.",
        ["选择要同步的规则"] = "Choose a rule to sync",
        ["选择后立即用这个规则同步。下次插入这块设备时，会默认选中上次使用的规则。"] =
            "The selected rule syncs right away. Next time this device is inserted, the last used rule is preselected.",
        ["用此规则同步"] = "Sync with this rule",
        ["删除规则？"] = "Delete rule?",
        ["删除规则"] = "Delete rule",
        ["“{0}”的同步内容列表会被删除，已经同步的文件不会受影响。"] =
            "The sync items of “{0}” will be deleted. Files already synced are not affected.",
        ["规则名称不能为空。"] = "Rule name cannot be empty.",
        ["已经有同名规则了。"] = "A rule with this name already exists.",
        ["至少要保留一个规则，SyncStick 才知道要同步什么。"] =
            "Keep at least one rule so SyncStick knows what to sync.",
        ["正在编辑当前规则的同步内容。"] = "Editing the sync items of the active rule.",
        ["默认规则"] = "Default rule",
        ["新规则"] = "New rule",

        // ------------------------------------------------------ Core / Engine
        ["未命名设备"] = "Unnamed device",
        ["未命名 U 盘"] = "Unnamed USB drive",
        ["无序列号"] = "No serial number",
        ["未命名项目"] = "Unnamed item",
        ["活动记录路径无效。"] = "Invalid activity path.",
        ["配置文件路径无效。"] = "Invalid settings path.",
        ["目标设备已经断开。"] = "The target device was disconnected.",
        ["同步过程中 U 盘被拔出，已停止写入。"] =
            "The USB drive was unplugged during the sync; writing stopped.",
        ["正在清理目标中多余的文件…"] = "Cleaning up extra files on the target…",
        ["设备尚未就绪。"] = "The device is not ready.",
        ["无法解析目标路径。"] = "Could not resolve the target path.",
        ["正在扫描来源…"] = "Scanning sources…",
        ["U 盘已连接。自动同步已暂停，可在总览中手动同步。"] =
            "USB drive connected. Auto-sync is paused; you can sync manually from Overview.",
        ["U 盘已连接，但还没有选择要同步的内容。"] =
            "USB drive connected, but no content to sync has been selected.",
        ["目标设备已断开，正在停止同步。"] =
            "The target device was disconnected; stopping the sync.",
        ["同步已取消。"] = "Sync cancelled.",
        ["同步未能完成。"] = "The sync could not be completed.",
        ["已是最新，无需写入。"] = "Everything is up to date; nothing to write.",
        ["请查看活动记录。"] = "Check the activity log.",
        ["找不到目标设备或来源文件夹。"] = "Target device or source folder not found.",
        ["没有权限写入目标设备，请检查 U 盘是否开启了写保护。"] =
            "No permission to write to the target device. Check whether the USB drive is write-protected.",
        ["应用正在退出。"] = "The app is exiting.",
        ["还没有选择目标设备。"] = "No target device has been selected.",
        ["检测到多块相似设备，请在“目标设备”中重新选择。"] =
            "Multiple similar devices detected. Reselect one in “Devices”.",
        ["目标 U 盘当前未连接。"] = "The target USB drive is not connected.",
        ["目标子目录必须是相对路径，不能包含盘符。"] =
            "The destination subfolder must be a relative path without a drive letter.",
        ["目标子目录必须是相对路径，不能以反斜杠开头。"] =
            "The destination subfolder must be a relative path and cannot start with a backslash.",
        ["目标子目录不能包含“..”。"] = "The destination subfolder cannot contain “..”.",
        ["目标路径不能包含“..”。"] = "The destination path cannot contain “..”.",
        ["目标路径超出了设备根目录。"] = "The destination path is outside the device root.",
    };
}
