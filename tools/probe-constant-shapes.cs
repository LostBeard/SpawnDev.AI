#:project D:/users/tj/Projects/SpawnDev.ILGPU.ML/SpawnDev.ILGPU.ML/SpawnDev.ILGPU.ML/SpawnDev.ILGPU.ML.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
using SpawnDev.ILGPU.ML;
using SpawnDev.ILGPU.ML.Onnx;

// What SHAPE does a Constant node actually carry, and does the initializer agree?
//
// ⚠️ ConstantOperator.InferOutputShapes returns [1] UNCONDITIONALLY, so while the node is present the
// compiler sees every Constant output as a 1-element tensor. Removing the node makes the shape come from
// the initializer instead. MEASURED 2026-09-03: that disagreement crashed ZipVoice's text encoder -
// "Node 46/1567 Reshape ... shapes=([1]; [0])" - so the two sources must be compared directly before the
// elimination pass can be re-landed.
var dir = Path.Combine(Path.GetTempPath(), "spawndev-onnx-probe");
var file = args.Length > 0 ? args[0] : "main_zipvoice_distill_text_encoder_int8.onnx";
var p = Path.Combine(dir, file);
if (!File.Exists(p)) { Console.WriteLine($"MISSING {p}"); return; }

var info = OnnxLoader.ParseModelInfo(File.ReadAllBytes(p));
var mg = InferenceSession.ConvertToModelGraph(info);

int rank0 = 0, empty = 0, agree = 0, disagree = 0, missing = 0, total = 0;
foreach (var node in info.Nodes.Where(n => n.OpType == "Constant" && n.Outputs.Length > 0))
{
    total++;
    var name = node.Outputs[0];
    bool has = mg.Initializers.TryGetValue(name, out var shape);
    if (!has) { missing++; continue; }
    int elems = shape.Length == 0 ? 1 : shape.Aggregate(1, (a, b) => a * b);
    if (shape.Length == 0) rank0++;
    if (elems == 0) empty++;
    if (elems == 1) agree++; else disagree++;
    if (empty <= 6 && elems == 0)
        Console.WriteLine($"  EMPTY  {name}  initializer shape=[{string.Join(",", shape)}]");
    if (name.EndsWith("Constant_23_output_0"))
        Console.WriteLine($"  >>> {name}: initializer shape=[{string.Join(",", shape)}] elems={elems}");
}
Console.WriteLine($"{file}: {total} Constant nodes | initializer MISSING {missing} | rank-0 {rank0} "
                + $"| 0-element {empty} | 1-element {agree} | multi-element {disagree}");
Console.WriteLine($"  -> ConstantOperator reports [1] for ALL of them, so {disagree} disagree with the "
                + "initializer today and only the 1-element ones are shape-neutral to remove.");
