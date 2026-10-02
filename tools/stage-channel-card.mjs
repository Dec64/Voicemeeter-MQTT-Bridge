// Stage only browser modules and attribution; never copy settings, tests or credentials.
import { mkdir, readdir, readFile, writeFile } from "node:fs/promises";
import { resolve, join } from "node:path";
import { fileURLToPath } from "node:url";
import { createHash } from "node:crypto";

if (process.argv.length !== 3) throw new Error("Usage: node tools/stage-channel-card.mjs NEW_OUTPUT_DIRECTORY");
const repository = fileURLToPath(new URL("../", import.meta.url));
const source = join(repository, "frontend", "channel-card", "src");
const output = resolve(process.argv[2]);
const files = (await readdir(source)).filter(name => name.endsWith(".js")).sort();
const contents = await Promise.all(files.map(async name => [name, await readFile(join(source, name))]));
contents.push(["LICENSE", await readFile(join(repository, "LICENSE"))]);
// A fresh directory prevents partial overwrites of an installed version. Use a new
// directory URL for every build so relative imports cannot reuse old cached modules.
await mkdir(output);
const manifest = { entrypoint: "voicemeeter-channel-card.js", files: {} };
for (const [name, bytes] of contents) {
  await writeFile(join(output, name), bytes, { flag: "wx" });
  manifest.files[name] = createHash("sha256").update(bytes).digest("hex");
}
await writeFile(join(output, "manifest.json"), JSON.stringify(manifest, null, 2) + "\n", { flag: "wx" });
console.log(JSON.stringify({ output, entrypoint: manifest.entrypoint, files: contents.length }));
