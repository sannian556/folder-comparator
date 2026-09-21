// 程序集信息（版本信息）—— 会写进 exe 的"属性 → 详细信息"里。
// 缺版本信息的小 exe 在杀毒软件的启发式/云查杀里会被当成"来路不明的程序"，
// 所以这里把标题、产品名、版本、版权补齐（两个编译目标共用这一份）。
// 想换成自己的名字/网名，改 Company 和 Copyright 这两行即可。
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("文件比较器 — 文件夹对比工具")]
[assembly: AssemblyDescription("比较两个文件夹，找出内容不同 / 仅在 A 中 / 仅在 B 中 / 完全相同的文件，支持逐行查看差异并导出 ZIP")]
[assembly: AssemblyProduct("文件比较器")]
[assembly: AssemblyCompany("文件比较器")]
[assembly: AssemblyCopyright("Copyright (C) 2026")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1")]
[assembly: ComVisible(false)]
[assembly: Guid("7f2c1a54-8d3b-4e6a-9c11-2b7e5d4a6f31")]
