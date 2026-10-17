using System;
using System.Collections.Generic;
using Hub12.Bytecode_Library.Bytecode;

namespace Hub12.Bytecode_Library.IR
{
    /// <summary>
    /// Checks basic IR graph invariants around transformation passes. This is a build-time
    /// validator: it does not change emitted code and deliberately avoids heuristic analysis.
    /// </summary>
    public static class ChunkValidator
    {
        public static void ValidateTree(Chunk root, string stage)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var visited = new HashSet<Chunk>();
            ValidateChunk(root, stage ?? "IR", visited);
        }

        private static void ValidateChunk(Chunk chunk, string stage, HashSet<Chunk> visited)
        {
            if (chunk == null || !visited.Add(chunk)) return;
            if (chunk.Instructions == null || chunk.Constants == null || chunk.Functions == null)
                throw new InvalidOperationException("12hub IR validation failed (" + stage + "): a chunk has a missing collection.");

            var members = new HashSet<Instruction>(chunk.Instructions);
            for (int index = 0; index < chunk.Instructions.Count; index++)
            {
                Instruction instruction = chunk.Instructions[index];
                if (instruction == null)
                    throw new InvalidOperationException("12hub IR validation failed (" + stage + "): null instruction at " + index + ".");

                if (instruction.OpCode == Opcode.Jmp || instruction.OpCode == Opcode.ForPrep || instruction.OpCode == Opcode.ForLoop)
                {
                    Instruction target = instruction.RefOperands != null && instruction.RefOperands.Length > 0
                        ? instruction.RefOperands[0] as Instruction
                        : null;
                    if (target == null || !members.Contains(target))
                        throw new InvalidOperationException(
                            "12hub IR validation failed (" + stage + "): dangling branch target in " + instruction.OpCode + " at instruction " + index + ".");
                }
            }

            foreach (Chunk child in chunk.Functions)
                ValidateChunk(child, stage, visited);
        }
    }
}
