// Unset ELECTRON_RUN_AS_NODE before launching electron-vite.
// VS Code sets this in its integrated terminal, which forces Electron
// to run as a plain Node.js interpreter instead of a browser app.
delete process.env.ELECTRON_RUN_AS_NODE;

const { execSync } = require("child_process");
const args = process.argv.slice(2).join(" ");
execSync(`electron-vite ${args}`, { stdio: "inherit", env: process.env });
