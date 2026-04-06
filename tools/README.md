# Tools 目录说明

此目录需要包含 Mermaid CLI 工具才能正常渲染图表。

## 安装步骤

1. 下载 Node.js 便携版 (Portable)
   - 访问 https://nodejs.org/en/download/
   - 下载 Windows Binary (.zip)
   - 解压到此目录，重命名文件夹为 `node`

2. 安装 Mermaid CLI
   ```cmd
   cd tools
   ..\tools\node\npm install @mermaid-js/mermaid-cli
   ```

## 目录结构

安装完成后，目录结构应该是：

```
tools/
├── mmdc.cmd          # Mermaid CLI 启动脚本
├── node/             # Node.js 便携版
│   ├── node.exe
│   └── npm.cmd
└── node_modules/     # Mermaid CLI 及依赖
    └── @mermaid-js/
        └── mermaid-cli/
```

## 测试

运行以下命令测试是否安装成功：

```cmd
tools\mmdc.cmd --version
```
