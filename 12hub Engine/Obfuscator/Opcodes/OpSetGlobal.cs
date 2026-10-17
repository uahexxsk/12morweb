using Hub12.Bytecode_Library.Bytecode;
using Hub12.Bytecode_Library.IR;

namespace Hub12.Obfuscator.Opcodes
{
	public class OpSetGlobal : VOpcode
	{
		public override bool IsInstruction(Instruction instruction) =>
			instruction.OpCode == Opcode.SetGlobal;

		public override string GetObfuscated(ObfuscationContext context) =>
			"Env[Const[Inst[OP_B]]]=Stk[Inst[OP_A]];";

		public override void Mutate(Instruction instruction) =>
			instruction.B++;
	}
}