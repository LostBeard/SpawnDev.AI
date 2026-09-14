#:package Microsoft.Playwright@1.49.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
// OPFS WRITE AMPLIFICATION probe: how much quota does writing an N-byte file actually consume WHILE it is
// being written?
//
// 🔴 WHY IT EXISTS. drive-ai-demo failed with "would cause the application to exceed its storage quota" in
// a profile with 3 GiB free, downloading a 1.71 GiB model. 1.71 < 3, so either the message is wrong or the
// write costs more than the file. FileSystemFileHandle.createWritable() is specified to write through a
// SWAP FILE - the data goes to a temporary copy and is swapped in on close() - so peak usage can be ~2x
// the final size. createSyncAccessHandle() writes IN PLACE and has no such cost, but it exists only in a
// DEDICATED worker.
//
// If the multiplier is ~2x, that is a real constraint on the browser model cache, not a harness artifact:
// a user needs twice the model size free, and a shared worker (no sync handle) cannot avoid it.
//
//   dotnet run tools/check-opfs-write-amplification.cs -- [url] [sizeMiB]
using Microsoft.Playwright;

var url = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "http://localhost:5199/";
var sizeMiB = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 512;

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true, Channel = "chrome" });
var page = await browser.NewPageAsync();
page.Console += (_, msg) => { if (msg.Type == "error") Console.WriteLine($"  [console error] {msg.Text}"); };
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });

Console.WriteLine($"[amp] writing {sizeMiB} MiB via createWritable and sampling quota usage");

var json = await page.EvaluateAsync<string>(@"async (sizeMiB) => {
  const root = await navigator.storage.getDirectory();
  try { await root.removeEntry('amp-probe.bin'); } catch {}

  const est0 = await navigator.storage.estimate();
  const handle = await root.getFileHandle('amp-probe.bin', { create: true });
  const w = await handle.createWritable();

  const chunk = new Uint8Array(16 * 1024 * 1024);   // 16 MiB, the size the model cache writes
  let written = 0, peak = 0;
  for (let i = 0; i < sizeMiB / 16; i++) {
    await w.write(chunk);
    written += chunk.length;
    const e = await navigator.storage.estimate();
    if (e.usage > peak) peak = e.usage;
  }
  const beforeClose = await navigator.storage.estimate();
  await w.close();
  const afterClose = await navigator.storage.estimate();

  const size = (await handle.getFile()).size;
  try { await root.removeEntry('amp-probe.bin'); } catch {}
  return JSON.stringify({
    base: est0.usage, written, fileSize: size,
    peakDuringWrite: peak, beforeClose: beforeClose.usage, afterClose: afterClose.usage,
    quota: est0.quota
  });
}", sizeMiB);

var d = System.Text.Json.JsonDocument.Parse(json).RootElement;
double MB(string k) => d.GetProperty(k).GetDouble() / 1048576.0;
double written = MB("written"), peak = MB("peakDuringWrite") - MB("base");
double after = MB("afterClose") - MB("base");

Console.WriteLine($"[amp] file written       : {written:F0} MiB (final size {MB("fileSize"):F0} MiB)");
Console.WriteLine($"[amp] peak usage delta   : {peak:F0} MiB");
Console.WriteLine($"[amp] usage after close  : {after:F0} MiB");
Console.WriteLine($"[amp] MULTIPLIER (peak/file): {peak / Math.Max(1, written):F2}x");
Console.WriteLine(peak > written * 1.5
    ? "[amp] AMPLIFIED - createWritable costs materially more than the file while writing. A user needs "
      + "that much headroom, and a shared worker (no createSyncAccessHandle) cannot avoid it."
    : "[amp] NOT amplified - peak tracks the file size, so quota failures are about total space, not the write path.");
