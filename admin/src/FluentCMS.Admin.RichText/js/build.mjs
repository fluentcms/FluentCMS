// Bundles the editor into one ES module, ../wwwroot/rich-text.js, which is committed: dotnet build and the image need no Node,
// and the CI job of the editor rebuilds it and fails on any difference (admin spec AQ7). Run it after a change to src/ or to
// a package, with the versions of package-lock.json (npm ci).
import { build } from "esbuild";

await build({
    entryPoints: ["src/index.js"],
    outfile: "../wwwroot/rich-text.js",
    bundle: true,
    format: "esm",
    platform: "browser",
    target: "es2022",
    minify: true,
    legalComments: "eof",
    charset: "utf8",
    banner: { js: "// The rich text editor of the FluentCMS admin, built by js/build.mjs; Tiptap and ProseMirror are MIT licensed (THIRD-PARTY-NOTICES.md)." },
    logLevel: "warning",
});
