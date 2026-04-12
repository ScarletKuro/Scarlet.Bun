import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { minify } from "terser";
import * as sass from "sass";

const scriptFilename = fileURLToPath(import.meta.url);
const scriptDirectory = path.dirname(scriptFilename);
const jsInputDir = path.join(scriptDirectory, "assets/scripts");
const jsOutputFile = path.join(scriptDirectory, "wwwroot/js/bundle.min.js");
const scssInput = path.join(scriptDirectory, "assets/styles/style.scss");
const scssOutput = path.join(scriptDirectory, "wwwroot/css/style.min.css");

async function buildJS() {
  console.log("Building JavaScript bundle...");

  if (!fs.existsSync(jsInputDir)) {
    console.error("JS directory missing:", jsInputDir);
    process.exit(1);
  }

  let files = fs
    .readdirSync(jsInputDir)
    .filter((f) => f.endsWith(".js"))
    .sort();

  if (files.length === 0) {
    console.error("No JS files found:", jsInputDir);
    process.exit(1);
  }

  // Concatenate files
  let code = "";
  for (const file of files) {
    const filePath = path.join(jsInputDir, file);
    console.log("  Adding:", file);
    code += fs.readFileSync(filePath, "utf-8") + "\n";
  }

  // Minify
  const minified = await minify(code);

  // Write JS bundle
  const outDir = path.dirname(jsOutputFile);
  if (!fs.existsSync(outDir)) fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(jsOutputFile, minified.code, "utf-8");
  console.log("✓ JavaScript bundle created:", jsOutputFile);
}

function buildSCSS() {
  console.log("Building SCSS bundle...");

  if (!fs.existsSync(scssInput)) {
    console.error("SCSS file missing:", scssInput);
    process.exit(1);
  }

  const result = sass.compile(scssInput, {
    style: "compressed",
    sourceMap: false,
    silenceDeprecations: ["import", "global-builtin"],
  });

  // Write SCSS bundle
  const outDir = path.dirname(scssOutput);
  if (!fs.existsSync(outDir)) fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(scssOutput, result.css);
  console.log("✓ CSS bundle created:", scssOutput);
}

await buildJS();
buildSCSS();

console.log("\n✓ Build completed successfully!");
