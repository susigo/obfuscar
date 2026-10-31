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

using System.IO;
using System.Linq;
using LeXtudio.Metadata.Mutable;
using Xunit;

namespace ObfuscarTests
{
    public class ControlFlowFlattenTests
    {
        private static string BuildXml(string outputPath, bool flattenCff,
            int minInstructions = 4, string moduleFile = "AssemblyWithStrings.dll",
            bool injectBait = false)
        {
            return string.Format(
                @"<?xml version='1.0'?>" +
                @"<Obfuscator>" +
                @"<Var name='InPath' value='{0}' />" +
                @"<Var name='OutPath' value='{1}' />" +
                @"<Var name='HideStrings' value='false' />" +
                @"<Var name='FlattenControlFlow' value='{2}' />" +
                @"<Var name='CffMinInstructions' value='{3}' />" +
                @"<Var name='BaitSeed' value='42' />" +
                @"<Var name='InjectBait' value='{5}' />" +
                @"<Var name='BaitTypeCount' value='8' />" +
                @"<Var name='BaitIntoMethods' value='false' />" +
                @"<Module file='{0}{4}{6}' />" +
                @"</Obfuscator>",
                TestHelper.InputPath, outputPath,
                flattenCff ? "true" : "false",
                minInstructions,
                Path.DirectorySeparatorChar,
                injectBait ? "true" : "false",
                moduleFile);
        }

        [Fact]
        public void CheckNoCffWhenDisabled()
        {
            string outputPath = TestHelper.OutputPath;
            TestHelper.BuildAndObfuscate("AssemblyWithStrings", string.Empty,
                BuildXml(outputPath, false), false,
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7);

            var assmDef = AssemblyDefinition.ReadAssembly(
                Path.Combine(outputPath, "AssemblyWithStrings.dll"));

            int switchCount = CountSwitchInstructions(assmDef);
            Assert.Equal(0, switchCount);
        }

        [Fact]
        public void CheckCffIntroducesSwitchDispatcher()
        {
            string outputPath = TestHelper.OutputPath;
            TestHelper.BuildAndObfuscate("AssemblyWithStrings", string.Empty,
                BuildXml(outputPath, true, 4), false,
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7);

            var assmDef = AssemblyDefinition.ReadAssembly(
                Path.Combine(outputPath, "AssemblyWithStrings.dll"));

            int switchCount = CountSwitchInstructions(assmDef);
            Assert.True(switchCount > 0,
                "Expected switch dispatcher instructions from CFF");
        }

        [Fact]
        public void CheckCffIntroducesXorStateTransitions()
        {
            string outputPath = TestHelper.OutputPath;
            TestHelper.BuildAndObfuscate("AssemblyWithStrings", string.Empty,
                BuildXml(outputPath, true, 4), false,
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7);

            var assmDef = AssemblyDefinition.ReadAssembly(
                Path.Combine(outputPath, "AssemblyWithStrings.dll"));

            int xorCount = 0;
            foreach (var type in assmDef.MainModule.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (method.Body == null) continue;
                    foreach (var instr in method.Body.Instructions)
                    {
                        if (instr.OpCode == MutableOpCodes.Xor)
                            xorCount++;
                    }
                }
            }

            Assert.True(xorCount > 0,
                "Expected XOR instructions from CFF state transitions");
        }

        [Fact]
        public void CheckFlattenedAssemblyExecutesCorrectly()
        {
            string outputPath = TestHelper.OutputPath;
            TestHelper.BuildAndObfuscate("AssemblyWithBranches", string.Empty,
                BuildXml(outputPath, true, 4, "AssemblyWithBranches.dll",
                    injectBait: true), false,
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7);

            string dllPath = Path.Combine(outputPath, "AssemblyWithBranches.dll");

            // The public method must actually have been flattened, otherwise
            // the invoke below proves nothing.
            var assmDef = AssemblyDefinition.ReadAssembly(dllPath);
            var score = assmDef.MainModule.Types
                .SelectMany(t => t.Methods)
                .FirstOrDefault(m => m.Name == "Score");
            Assert.NotNull(score);
            Assert.NotNull(score.Body);
            Assert.Contains(score.Body.Instructions,
                i => i.OpCode == MutableOpCodes.Switch);

            // Invoking the flattened method previously hit an infinite
            // dispatcher loop or InvalidProgramException.
            var asm = System.Reflection.Assembly.Load(File.ReadAllBytes(dllPath));
            var branchy = asm.GetType("TestClasses.Branchy");
            Assert.NotNull(branchy);

            Assert.Equal(46, branchy.GetMethod("Score").Invoke(null, new object[] { 7 }));
            Assert.Equal(6, branchy.GetMethod("DoubleIt").Invoke(null, new object[] { 3 }));
        }

        [Fact]
        public void CheckMinInstructionsThresholdRespected()
        {
            string outputPath = TestHelper.OutputPath;
            TestHelper.BuildAndObfuscate("AssemblyWithStrings", string.Empty,
                BuildXml(outputPath, true, 9999), false,
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7);

            var assmDef = AssemblyDefinition.ReadAssembly(
                Path.Combine(outputPath, "AssemblyWithStrings.dll"));

            int switchCount = CountSwitchInstructions(assmDef);
            Assert.Equal(0, switchCount);
        }

        private static int CountSwitchInstructions(AssemblyDefinition assembly)
        {
            int count = 0;
            foreach (var type in assembly.MainModule.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (method.Body == null) continue;
                    foreach (var instr in method.Body.Instructions)
                    {
                        if (instr.OpCode == MutableOpCodes.Switch)
                            count++;
                    }
                }
            }

            return count;
        }
    }
}
