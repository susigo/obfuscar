#region Copyright (c) 2007 Ryan Williams <drcforbin@gmail.com>

/// <copyright>
/// Copyright (c) 2007 Ryan Williams <drcforbin@gmail.com>
///
/// Permission is hereby granted, free of charge, to any person obtaining a copy
/// of this software and associated documentation files (the "Software"), to deal
/// in the Software without restriction, including without limitation the rights
/// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
/// copies of the Software, and to permit persons to whom the Software is
/// furnished to do so, subject to the following conditions:
///
/// The above copyright notice and this permission notice shall be included in
/// all copies or substantial portions of the Software.
///
/// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
/// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
/// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
/// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
/// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
/// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
/// THE SOFTWARE.
/// </copyright>

#endregion

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using LeXtudio.Metadata.Mutable;
using AssemblyDefinition = LeXtudio.Metadata.Mutable.MutableAssemblyDefinition;
using Instruction = LeXtudio.Metadata.Mutable.MutableInstruction;
using MethodDefinition = LeXtudio.Metadata.Mutable.MutableMethodDefinition;
using OpCodes = LeXtudio.Metadata.Mutable.MutableOpCodes;
using TypeDefinition = LeXtudio.Metadata.Mutable.MutableTypeDefinition;
using VariableDefinition = LeXtudio.Metadata.Mutable.MutableVariableDefinition;

namespace Obfuscar
{
    internal class ControlFlowFlattener
    {
        private sealed class BasicBlock
        {
            public int Index;
            public int StartIndex;
            public int EndIndex;
            public int StateValue;
            public Instruction Label;
        }

        private readonly AssemblyDefinition _assembly;
        private readonly Settings _settings;
        private readonly Random _rng;

        public ControlFlowFlattener(AssemblyDefinition assembly, Settings settings)
        {
            _assembly = assembly;
            _settings = settings;
            _rng = settings.BaitSeed < 0 ? new Random() : new Random(settings.BaitSeed ^ 0x46_4C_41_54);
        }

        public void Flatten()
        {
            var module = _assembly.MainModule;
            int flattened = 0;

            foreach (var type in AllTypes(module))
            {
                foreach (var method in type.Methods)
                {
                    if (!CanFlatten(method))
                        continue;

                    try
                    {
                        FlattenMethod(method, module);
                        flattened++;
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Logger.LogDebug(
                            "Skipping CFF for {0}: {1}", method.FullName, ex.Message);
                    }
                }
            }

            LoggerService.Logger.LogInformation(
                "Control-flow flattened {0} methods in {1}", flattened, _assembly.Name?.Name);
        }

        private static IEnumerable<TypeDefinition> AllTypes(MutableModuleDefinition module)
        {
            var stack = new Stack<TypeDefinition>(module.Types);
            while (stack.Count > 0)
            {
                var type = stack.Pop();
                yield return type;
                foreach (var nested in type.NestedTypes)
                    stack.Push(nested);
            }
        }

        private bool CanFlatten(MethodDefinition method)
        {
            if (method.Body == null || method.IsConstructor || method.IsPInvokeImpl)
                return false;

            if (method.Body.ExceptionHandlers.Count > 0)
                return false;

            if (method.Body.Instructions.Count < _settings.CffMinInstructions)
                return false;

            if (method.Name == ".cctor")
                return false;

            return true;
        }

        private void FlattenMethod(MethodDefinition method, MutableModuleDefinition module)
        {
            var originalInstructions = new List<Instruction>(method.Body.Instructions);
            if (originalInstructions.Count == 0)
                return;

            var blocks = IdentifyBasicBlocks(originalInstructions);
            if (blocks.Count < 2)
                return;

            AssignStateValues(blocks);

            var stateVar = new VariableDefinition(module.TypeSystem.Int32);
            method.Body.Variables.Add(stateVar);
            method.Body.InitLocals = true;

            var worker = method.GetILProcessor();
            method.Body.Instructions.Clear();

            int methodKey = _rng.Next(0x1000, int.MaxValue);

            EmitStateInit(worker, blocks[0].StateValue, methodKey, stateVar);

            var loopStart = worker.Create(OpCodes.Nop);
            worker.Append(loopStart);

            worker.Emit(OpCodes.Ldloc, stateVar);
            worker.Emit(OpCodes.Ldc_I4, methodKey);
            worker.Emit(OpCodes.Xor);

            var switchTargets = new Instruction[blocks.Count];
            for (int i = 0; i < blocks.Count; i++)
            {
                var nop = worker.Create(OpCodes.Nop);
                blocks[i].Label = nop;
                switchTargets[i] = nop;
            }

            worker.Append(worker.Create(OpCodes.Switch, switchTargets));
            worker.Append(worker.Create(OpCodes.Br, loopStart));

            var branchTargetMap = BuildBranchTargetMap(originalInstructions, blocks);

            for (int bi = 0; bi < blocks.Count; bi++)
            {
                var block = blocks[bi];
                worker.Append(block.Label);

                int lastIdx = block.EndIndex - 1;
                var terminator = originalInstructions[lastIdx];
                var flow = terminator.OpCode.FlowControl;

                for (int ii = block.StartIndex; ii < block.EndIndex; ii++)
                {
                    var instr = originalInstructions[ii];
                    bool isTerminator = (ii == lastIdx);

                    if (isTerminator && IsBranch(flow))
                    {
                        EmitFlattenedTerminator(worker, instr, flow, blocks, block, bi,
                            branchTargetMap, loopStart, methodKey, stateVar);
                    }
                    else if (isTerminator && flow == MutableFlowControl.Return)
                    {
                        worker.Append(instr);
                    }
                    else if (isTerminator && flow == MutableFlowControl.Throw)
                    {
                        worker.Append(instr);
                    }
                    else
                    {
                        worker.Append(instr);

                        if (isTerminator && bi + 1 < blocks.Count)
                        {
                            EmitStateTransition(worker, blocks[bi + 1].StateValue,
                                methodKey, stateVar, loopStart);
                        }
                    }
                }
            }

            if (method.Body.MaxStackSize < 16)
                method.Body.MaxStackSize = 16;
        }

        private List<BasicBlock> IdentifyBasicBlocks(List<Instruction> instructions)
        {
            var leaders = new HashSet<int> { 0 };

            for (int i = 0; i < instructions.Count; i++)
            {
                var instr = instructions[i];
                var flow = instr.OpCode.FlowControl;

                if (flow == MutableFlowControl.Branch ||
                    flow == MutableFlowControl.Cond_Branch ||
                    flow == MutableFlowControl.Return ||
                    flow == MutableFlowControl.Throw)
                {
                    if (i + 1 < instructions.Count)
                        leaders.Add(i + 1);
                }

                if (flow == MutableFlowControl.Branch || flow == MutableFlowControl.Cond_Branch)
                {
                    if (instr.Operand is Instruction target)
                    {
                        int idx = instructions.IndexOf(target);
                        if (idx >= 0) leaders.Add(idx);
                    }
                    else if (instr.Operand is Instruction[] targets)
                    {
                        foreach (var t in targets)
                        {
                            int idx = instructions.IndexOf(t);
                            if (idx >= 0) leaders.Add(idx);
                        }
                    }
                }
            }

            var sorted = new List<int>(leaders);
            sorted.Sort();

            var blocks = new List<BasicBlock>();
            for (int i = 0; i < sorted.Count; i++)
            {
                int start = sorted[i];
                int end = (i + 1 < sorted.Count) ? sorted[i + 1] : instructions.Count;
                blocks.Add(new BasicBlock
                {
                    Index = blocks.Count,
                    StartIndex = start,
                    EndIndex = end
                });
            }

            return blocks;
        }

        private void AssignStateValues(List<BasicBlock> blocks)
        {
            var used = new HashSet<int>();
            foreach (var block in blocks)
            {
                int sv;
                do { sv = _rng.Next(0, blocks.Count * 4); } while (!used.Add(sv));
                block.StateValue = sv;
            }
        }

        private static Dictionary<Instruction, int> BuildBranchTargetMap(
            List<Instruction> instructions, List<BasicBlock> blocks)
        {
            var instrToBlock = new Dictionary<Instruction, int>();
            foreach (var block in blocks)
                instrToBlock[instructions[block.StartIndex]] = block.Index;
            return instrToBlock;
        }

        private void EmitFlattenedTerminator(
            MutableILProcessor worker, Instruction terminator, MutableFlowControl flow,
            List<BasicBlock> blocks, BasicBlock currentBlock, int currentIndex,
            Dictionary<Instruction, int> targetMap, Instruction loopStart,
            int methodKey, VariableDefinition stateVar)
        {
            if (flow == MutableFlowControl.Branch)
            {
                if (terminator.Operand is Instruction target && targetMap.TryGetValue(target, out int bi))
                {
                    EmitStateTransition(worker, blocks[bi].StateValue, methodKey, stateVar, loopStart);
                }
                else if (currentIndex + 1 < blocks.Count)
                {
                    EmitStateTransition(worker, blocks[currentIndex + 1].StateValue,
                        methodKey, stateVar, loopStart);
                }
            }
            else if (flow == MutableFlowControl.Cond_Branch)
            {
                if (terminator.Operand is Instruction[] switchTargets)
                {
                    EmitFlattenedSwitch(worker, terminator, switchTargets, blocks,
                        currentBlock, currentIndex, targetMap, loopStart, methodKey, stateVar);
                }
                else if (terminator.Operand is Instruction condTarget &&
                         targetMap.TryGetValue(condTarget, out int targetBi))
                {
                    int fallthroughBi = (currentIndex + 1 < blocks.Count) ? currentIndex + 1 : 0;

                    var takenLabel = worker.Create(OpCodes.Nop);

                    worker.Append(worker.Create(terminator.OpCode, takenLabel));

                    EmitStateTransition(worker, blocks[fallthroughBi].StateValue,
                        methodKey, stateVar, loopStart);

                    worker.Append(takenLabel);
                    EmitStateTransition(worker, blocks[targetBi].StateValue,
                        methodKey, stateVar, loopStart);
                }
            }
        }

        private void EmitFlattenedSwitch(
            MutableILProcessor worker, Instruction original, Instruction[] originalTargets,
            List<BasicBlock> blocks, BasicBlock currentBlock, int currentIndex,
            Dictionary<Instruction, int> targetMap, Instruction loopStart,
            int methodKey, VariableDefinition stateVar)
        {
            var caseLabels = new Instruction[originalTargets.Length];
            for (int i = 0; i < originalTargets.Length; i++)
                caseLabels[i] = worker.Create(OpCodes.Nop);

            worker.Append(worker.Create(OpCodes.Switch, caseLabels));

            int defaultBi = (currentIndex + 1 < blocks.Count) ? currentIndex + 1 : 0;
            EmitStateTransition(worker, blocks[defaultBi].StateValue,
                methodKey, stateVar, loopStart);

            for (int i = 0; i < originalTargets.Length; i++)
            {
                worker.Append(caseLabels[i]);
                if (targetMap.TryGetValue(originalTargets[i], out int bi))
                {
                    EmitStateTransition(worker, blocks[bi].StateValue,
                        methodKey, stateVar, loopStart);
                }
                else
                {
                    EmitStateTransition(worker, blocks[defaultBi].StateValue,
                        methodKey, stateVar, loopStart);
                }
            }
        }

        private void EmitStateInit(MutableILProcessor worker, int stateValue,
            int methodKey, VariableDefinition stateVar)
        {
            worker.Emit(OpCodes.Ldc_I4, stateValue ^ methodKey);
            worker.Emit(OpCodes.Stloc, stateVar);
        }

        private void EmitStateTransition(MutableILProcessor worker, int targetState,
            int methodKey, VariableDefinition stateVar, Instruction loopStart)
        {
            int noise = _rng.Next(1, 0x7FFF);
            worker.Emit(OpCodes.Ldc_I4, (targetState ^ methodKey) + noise);
            worker.Emit(OpCodes.Ldc_I4, noise);
            worker.Emit(OpCodes.Sub);
            worker.Emit(OpCodes.Stloc, stateVar);
            worker.Append(worker.Create(OpCodes.Br, loopStart));
        }

        private static bool IsBranch(MutableFlowControl flow)
        {
            return flow == MutableFlowControl.Branch || flow == MutableFlowControl.Cond_Branch;
        }
    }
}
