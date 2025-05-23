// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.V8;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.ClearScript.Test
{
    [TestClass]
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable", Justification = "Test classes use TestCleanupAttribute for deterministic teardown.")]
    public class V8ModuleTest : ClearScriptTest
    {
        #region setup / teardown

        private V8ScriptEngine engine;

        [TestInitialize]
        public void TestInitialize()
        {
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDynamicModuleImports | V8ScriptEngineFlags.EnableDebugging);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            engine.Dispose();
            BaseTestCleanup();
        }

        #endregion

        #region test methods

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(123 + 456, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Arithmetic from 'JavaScript/StandardModule/Arithmetic/Arithmetic.js';
                import.meta.setResult(Arithmetic.Add(123, 456));
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_ForeignExtension()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(123 + 456, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Arithmetic from 'JavaScript/StandardModule/Arithmetic/Arithmetic.bogus';
                import.meta.setResult(Arithmetic.BogusAdd(123, 456));
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_MixedImport()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/GeometryWithDynamicImport.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_Nested()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/Geometry.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_Disabled()
        {
            TestUtil.AssertException<UnauthorizedAccessException>(() => engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/Geometry.js';
                new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_PathlessImport()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "StandardModule", "Arithmetic"),
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "StandardModule", "Geometry")
            );

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(123 + 456, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Arithmetic from 'Arithmetic.js';
                import.meta.setResult(Arithmetic.Add(123, 456));
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_PathlessImport_MixedImport()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "StandardModule", "Arithmetic"),
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "StandardModule", "Geometry")
            );

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'GeometryWithDynamicImport.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_PathlessImport_Nested()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "StandardModule", "Arithmetic"),
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "StandardModule", "Geometry")
            );

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'GeometryWithPathlessImport.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_PathlessImport_Disabled()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "StandardModule", "Arithmetic"),
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "StandardModule", "Geometry")
            );

            TestUtil.AssertException<UnauthorizedAccessException>(() => engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'Geometry.js';
                new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_DynamicImport()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            engine.Evaluate(@"
                (async function () {
                    try {
                        let Arithmetic = await import('JavaScript/StandardModule/Arithmetic/Arithmetic.js');
                        result = Arithmetic.Add(123, 456);
                    }
                    catch (exception) {
                        caughtException = exception;
                    }
                })();
            ");

            Assert.IsInstanceOfType(engine.Script.caughtException, typeof(Undefined));
            Assert.AreEqual(123 + 456, engine.Script.result);
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_DynamicImport_MixedImport()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            engine.Evaluate(@"
                (async function () {
                    try {
                        let Geometry = await import('JavaScript/StandardModule/Geometry/Geometry.js');
                        result = new Geometry.Square(25).Area;
                    }
                    catch (exception) {
                        caughtException = exception;
                    }
                })();
            ");

            Assert.IsInstanceOfType(engine.Script.caughtException, typeof(Undefined));
            Assert.AreEqual(25 * 25, engine.Script.result);
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_DynamicImport_Nested()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            engine.Evaluate(@"
                (async function () {
                    try {
                        let Geometry = await import('JavaScript/StandardModule/Geometry/GeometryWithDynamicImport.js');
                        result = new Geometry.Square(25).Area;
                    }
                    catch (exception) {
                        caughtException = exception;
                    }
                })();
            ");

            Assert.IsInstanceOfType(engine.Script.caughtException, typeof(Undefined));
            Assert.AreEqual(25 * 25, engine.Script.result);
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_DynamicImport_Disabled()
        {
            engine.Evaluate(@"
                (async function () {
                    try {
                        let Geometry = await import('JavaScript/StandardModule/Geometry/GeometryWithDynamicImport.js');
                        result = new Geometry.Square(25).Area;
                    }
                    catch (exception) {
                        caughtException = exception;
                    }
                })();
            ");

            Assert.IsNotInstanceOfType(engine.Script.caughtException, typeof(Undefined));
            TestUtil.AssertException<UnauthorizedAccessException>(() => engine.Execute("throw caughtException"));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_File_FileNameExtensions()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/Geometry';
                import.meta.setResult(new Geometry.Square(25).Area);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            Assert.AreEqual(123 + 456, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Arithmetic from 'https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Arithmetic/Arithmetic.js';
                import.meta.setResult(Arithmetic.Add(123, 456));
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web_MixedImport()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Geometry/GeometryWithDynamicImport.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web_Nested()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Geometry/Geometry.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web_Disabled()
        {
            TestUtil.AssertException<UnauthorizedAccessException>(() => engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Geometry/Geometry.js';
                new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web_PathlessImport()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Arithmetic",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Geometry"
            );

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            Assert.AreEqual(123 + 456, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Arithmetic from 'Arithmetic.js';
                import.meta.setResult(Arithmetic.Add(123, 456));
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web_PathlessImport_MixedImport()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Arithmetic",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Geometry"
            );

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'GeometryWithDynamicImport.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web_PathlessImport_Nested()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Arithmetic",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Geometry"
            );

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'GeometryWithPathlessImport.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web_PathlessImport_Disabled()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Arithmetic",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Geometry"
            );

            TestUtil.AssertException<UnauthorizedAccessException>(() => engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'Geometry.js';
                new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web_DynamicImport()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            engine.Evaluate(@"
                (async function () {
                    try {
                        let Arithmetic = await import('https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Arithmetic/Arithmetic.js');
                        result = Arithmetic.Add(123, 456);
                    }
                    catch (exception) {
                        caughtException = exception;
                    }
                })();
            ");

            Assert.IsInstanceOfType(engine.Script.caughtException, typeof(Undefined));
            Assert.AreEqual(123 + 456, engine.Script.result);
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web_DynamicImport_MixedImport()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            engine.Evaluate(@"
                (async function () {
                    try {
                        let Geometry = await import('https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Geometry/Geometry.js');
                        result = new Geometry.Square(25).Area;
                    }
                    catch (exception) {
                        caughtException = exception;
                    }
                })();
            ");

            Assert.IsInstanceOfType(engine.Script.caughtException, typeof(Undefined));
            Assert.AreEqual(25 * 25, engine.Script.result);
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web_DynamicImport_Nested()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            engine.Evaluate(@"
                (async function () {
                    try {
                        let Geometry = await import('https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Geometry/GeometryWithDynamicImport.js');
                        result = new Geometry.Square(25).Area;
                    }
                    catch (exception) {
                        caughtException = exception;
                    }
                })();
            ");

            Assert.IsInstanceOfType(engine.Script.caughtException, typeof(Undefined));
            Assert.AreEqual(25 * 25, engine.Script.result);
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web_DynamicImport_Disabled()
        {
            engine.Evaluate(@"
                (async function () {
                    try {
                        let Geometry = await import('https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Geometry/GeometryWithDynamicImport.js');
                        result = new Geometry.Square(25).Area;
                    }
                    catch (exception) {
                        caughtException = exception;
                    }
                })();
            ");

            Assert.IsNotInstanceOfType(engine.Script.caughtException, typeof(Undefined));
            TestUtil.AssertException<UnauthorizedAccessException>(() => engine.Execute("throw caughtException"));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Web_FileNameExtensions()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/StandardModule/Geometry/Geometry';
                import.meta.setResult(new Geometry.Square(25).Area);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Compilation()
        {
            var module = engine.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/Geometry.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            ");

            using (module)
            {
                engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
                Assert.AreEqual(25 * 25, engine.Evaluate(module));

                // re-evaluating a module is a no-op
                Assert.IsInstanceOfType(engine.Evaluate(module), typeof(Undefined));
            }
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Compilation_CodeCache()
        {
            var code = @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/Geometry.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            ";

            byte[] cacheBytes;
            using (engine.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, out cacheBytes))
            {
            }

            Assert.IsNotNull(cacheBytes);
            Assert.IsTrue(cacheBytes.Length > 350); // typical size is ~700

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDynamicModuleImports | V8ScriptEngineFlags.EnableDebugging);

            var module = engine.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, cacheBytes, out var cacheAccepted);
            Assert.IsTrue(cacheAccepted);

            using (module)
            {
                engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
                Assert.AreEqual(25 * 25, engine.Evaluate(module));

                // re-evaluating a module is a no-op
                Assert.IsInstanceOfType(engine.Evaluate(module), typeof(Undefined));
            }
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Compilation_Runtime()
        {
            engine.Dispose();

            using (var runtime = new V8Runtime(V8RuntimeFlags.EnableDebugging))
            {
                var module = runtime.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                    import * as Geometry from 'JavaScript/StandardModule/Geometry/Geometry.js';
                    import.meta.setResult(new Geometry.Square(25).Area);
                ");

                using (module)
                {
                    engine = runtime.CreateScriptEngine();

                    engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
                    Assert.AreEqual(25 * 25, engine.Evaluate(module));

                    // re-evaluating a module is a no-op
                    Assert.IsInstanceOfType(engine.Evaluate(module), typeof(Undefined));
                }
            }
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Compilation_Runtime_CodeCache()
        {
            var code = @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/Geometry.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            ";

            byte[] cacheBytes;
            using (engine.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, out cacheBytes))
            {
            }

            Assert.IsNotNull(cacheBytes);
            Assert.IsTrue(cacheBytes.Length > 350); // typical size is ~700

            engine.Dispose();

            using (var runtime = new V8Runtime(V8RuntimeFlags.EnableDebugging))
            {
                engine = runtime.CreateScriptEngine();

                var module = runtime.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, cacheBytes, out var cacheAccepted);
                Assert.IsTrue(cacheAccepted);

                using (module)
                {
                    engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
                    Assert.AreEqual(25 * 25, engine.Evaluate(module));

                    // re-evaluating a module is a no-op
                    Assert.IsInstanceOfType(engine.Evaluate(module), typeof(Undefined));
                }
            }
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_SideEffects()
        {
            using (var runtime = new V8Runtime())
            {
                runtime.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableAllLoading;
                var module = runtime.CompileDocument("JavaScript/StandardModule/ModuleWithSideEffects.js", ModuleCategory.Standard);

                using (var testEngine = runtime.CreateScriptEngine())
                {
                    testEngine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableAllLoading;
                    testEngine.Execute("foo = {}");
                    Assert.AreEqual(625, testEngine.Evaluate(module));
                    Assert.AreEqual(625, testEngine.Evaluate("foo.bar"));

                    // re-evaluating a module is a no-op
                    Assert.IsInstanceOfType(testEngine.Evaluate(module), typeof(Undefined));
                }

                using (var testEngine = runtime.CreateScriptEngine())
                {
                    testEngine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableAllLoading;
                    testEngine.Execute("foo = {}");
                    Assert.AreEqual(625, testEngine.Evaluate(module));
                    Assert.AreEqual(625, testEngine.Evaluate("foo.bar"));

                    // re-evaluating a module is a no-op
                    Assert.IsInstanceOfType(testEngine.Evaluate(module), typeof(Undefined));
                }
            }
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Caching()
        {
            Assert.AreEqual(0UL, engine.GetRuntimeStatistics().ModuleCount);

            var info = new DocumentInfo { Category = ModuleCategory.Standard };

            Assert.AreEqual(Math.PI, engine.Evaluate(info, "import.meta.setResult(Math.PI)"));
            Assert.AreEqual(Math.PI, engine.Evaluate(info, "import.meta.setResult(Math.PI)"));
            Assert.AreEqual(2UL, engine.GetRuntimeStatistics().ModuleCount);

            info = new DocumentInfo("Test") { Category = ModuleCategory.Standard };

            Assert.AreEqual(Math.E, engine.Evaluate(info, "import.meta.setResult(Math.E)"));
            Assert.IsInstanceOfType(engine.Evaluate(info, "import.meta.setResult(Math.E)"), typeof(Undefined));
            Assert.AreEqual(3UL, engine.GetRuntimeStatistics().ModuleCount);

            Assert.AreEqual(Math.PI, engine.Evaluate(info, "import.meta.setResult(Math.PI)"));
            Assert.IsInstanceOfType(engine.Evaluate(info, "import.meta.setResult(Math.PI)"), typeof(Undefined));
            Assert.AreEqual(4UL, engine.GetRuntimeStatistics().ModuleCount);

            using (var runtime = new V8Runtime())
            {
                for (var i = 0; i < 10; i++)
                {
                    using (var testEngine = runtime.CreateScriptEngine())
                    {
                        Assert.AreEqual(Math.PI, testEngine.Evaluate(info, "import.meta.setResult(Math.PI)"));
                        Assert.AreEqual(Math.E, testEngine.Evaluate(info, "import.meta.setResult(Math.E)"));
                        Assert.AreEqual(2UL, testEngine.GetStatistics().ModuleCount);
                    }
                }

                Assert.AreEqual(20UL, runtime.GetStatistics().ModuleCount);
            }

            using (var runtime = new V8Runtime())
            {
                for (var i = 0; i < 300; i++)
                {
                    using (var testEngine = runtime.CreateScriptEngine())
                    {
                        Assert.AreEqual(Math.PI, testEngine.Evaluate(info, "import.meta.setResult(Math.PI)"));
                        Assert.IsInstanceOfType(testEngine.Evaluate(info, "import.meta.setResult(Math.PI)"), typeof(Undefined));
                    }
                }

                Assert.AreEqual(300UL, runtime.GetStatistics().ModuleCount);
            }

            using (var runtime = new V8Runtime())
            {
                using (var testEngine = runtime.CreateScriptEngine())
                {
                    for (var i = 0; i < 300; i++)
                    {
                        Assert.AreEqual(Math.PI + i, testEngine.Evaluate(info, "import.meta.setResult(Math.PI" + "+" + i + ")"));
                    }

                    Assert.AreEqual(300UL, testEngine.GetStatistics().ModuleCount);
                    Assert.AreEqual(300UL, testEngine.GetStatistics().ModuleCacheSize);
                }

                Assert.AreEqual(300UL, runtime.GetStatistics().ModuleCount);
            }
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Context()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            engine.DocumentSettings.Loader = new CustomLoader();

            Assert.AreEqual(123, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/Geometry.js';
                import.meta.setResult(Geometry.Meta.foo)
            "));

            Assert.AreEqual(456.789, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/Geometry.js';
                import.meta.setResult(Geometry.Meta.bar)
            "));

            Assert.AreEqual("bogus", engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/Geometry.js';
                import.meta.setResult(Geometry.Meta.baz)
            "));

            Assert.IsInstanceOfType(engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/Geometry.js';
                import.meta.setResult(new Geometry.Meta.qux())
            "), typeof(Random));

            Assert.IsInstanceOfType(engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/Geometry.js';
                import.meta.setResult(Geometry.Meta.quux)
            "), typeof(Undefined));

            Assert.AreEqual(Math.PI, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Arithmetic from 'JavaScript/StandardModule/Arithmetic/Arithmetic.js';
                import.meta.setResult(Arithmetic.Meta.foo)
            "));

            Assert.IsInstanceOfType(engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Arithmetic from 'JavaScript/StandardModule/Arithmetic/Arithmetic.js';
                import.meta.setResult(Arithmetic.Meta.bar)
            "), typeof(Undefined));

            Assert.AreEqual(0, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModule/Geometry/GeometryWithDynamicImport.js';
                import.meta.setResult(Object.keys(Geometry.Meta).length);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_SystemDocument()
        {
            engine.DocumentSettings.AddSystemDocument("test", ModuleCategory.Standard, @"
                export function Add(a, b) {
                    return a + b;
                }
            ");

            dynamic add = engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Test from 'test';
                import.meta.setResult(Test.Add)
            ");

            Assert.AreEqual(579, add(123, 456));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_CircularReference()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModuleWithCycles/Geometry/Geometry.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_CircularReference_MixedImport()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import * as Geometry from 'JavaScript/StandardModuleWithCycles/Geometry/GeometryWithDynamicImport.js';
                import.meta.setResult(new Geometry.Square(25).Area);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_ImportCommonJS()
        {
            void OnDocumentLoad(ref DocumentInfo info)
            {
                var uri = info.Uri;
                if (uri.IsFile)
                {
                    var path = Path.GetDirectoryName(uri.AbsolutePath);
                    if (path.Split(Path.DirectorySeparatorChar).Contains("CommonJS"))
                    {
                        info.Category = ModuleCategory.CommonJS;
                    }
                }
            }

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading | DocumentAccessFlags.AllowCategoryMismatch;
            engine.DocumentSettings.LoadCallback = OnDocumentLoad;

            Assert.AreEqual(12, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import { Rectangle } from 'JavaScript/CommonJS/Geometry/Geometry';
                globalThis.Rectangle1 = Rectangle;
                import.meta.setResult(new Rectangle(3, 4).Area);
            "));

            Assert.AreEqual(30, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import { Rectangle } from 'JavaScript/CommonJS/Geometry/Geometry';
                globalThis.Rectangle2 = Rectangle;
                import.meta.setResult(new Rectangle(5, 6).Area);
            "));

            Assert.AreEqual(56, engine.Evaluate(@"
                (async function() {
                    let Rectangle = (await import('JavaScript/CommonJS/Geometry/Geometry')).Rectangle;
                    Rectangle3 = Rectangle;
                    return new Rectangle(7, 8).Area;
                })()
            ").ToTask().Result);

            Assert.AreEqual(engine.Script.Rectangle1, engine.Script.Rectangle2);
            Assert.AreEqual(engine.Script.Rectangle2, engine.Script.Rectangle3);
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_ImportCommonJS_AsyncLoadCallback()
        {
            async Task OnDocumentLoadAsync(ValueRef<DocumentInfo> info, Stream contents)
            {
                await Task.Delay(100).ConfigureAwait(false);

                var uri = info.Value.Uri;
                if (uri.IsFile)
                {
                    var path = Path.GetDirectoryName(uri.AbsolutePath);
                    if (path.Split(Path.DirectorySeparatorChar).Contains("CommonJS"))
                    {
                        info.Value.Category = ModuleCategory.CommonJS;
                    }
                }
            }

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading | DocumentAccessFlags.AllowCategoryMismatch | DocumentAccessFlags.UseAsyncLoadCallback;
            engine.DocumentSettings.AsyncLoadCallback = OnDocumentLoadAsync;

            Assert.AreEqual(12, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import { Rectangle } from 'JavaScript/CommonJS/Geometry/Geometry';
                globalThis.Rectangle1 = Rectangle;
                import.meta.setResult(new Rectangle(3, 4).Area);
            "));

            Assert.AreEqual(30, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import { Rectangle } from 'JavaScript/CommonJS/Geometry/Geometry';
                globalThis.Rectangle2 = Rectangle;
                import.meta.setResult(new Rectangle(5, 6).Area);
            "));

            Assert.AreEqual(56, engine.Evaluate(@"
                (async function() {
                    let Rectangle = (await import('JavaScript/CommonJS/Geometry/Geometry')).Rectangle;
                    Rectangle3 = Rectangle;
                    return new Rectangle(7, 8).Area;
                })()
            ").ToTask().Result);

            Assert.AreEqual(engine.Script.Rectangle1, engine.Script.Rectangle2);
            Assert.AreEqual(engine.Script.Rectangle2, engine.Script.Rectangle3);
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_ImportCommonJS_SystemDocument()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading | DocumentAccessFlags.AllowCategoryMismatch;
            engine.DocumentSettings.AddSystemDocument("Geometry", ModuleCategory.CommonJS, @"
                exports.Rectangle = class {
                    constructor(width, height) {
                        this.width = width;
                        this.height = height;
                    }
                    get Area() {
                        return this.width * this.height;
                    }
                }
            ");

            Assert.AreEqual(12, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import { Rectangle } from 'Geometry';
                globalThis.Rectangle1 = Rectangle;
                import.meta.setResult(new Rectangle(3, 4).Area);
            "));

            Assert.AreEqual(30, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                import { Rectangle } from 'Geometry';
                globalThis.Rectangle2 = Rectangle;
                import.meta.setResult(new Rectangle(5, 6).Area);
            "));

            Assert.AreEqual(56, engine.Evaluate(@"
                (async function() {
                    let Rectangle = (await import('Geometry')).Rectangle;
                    Rectangle3 = Rectangle;
                    return new Rectangle(7, 8).Area;
                })()
            ").ToTask().Result);

            Assert.AreEqual(engine.Script.Rectangle1, engine.Script.Rectangle2);
            Assert.AreEqual(engine.Script.Rectangle2, engine.Script.Rectangle3);
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Json_Object()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading | DocumentAccessFlags.AllowCategoryMismatch;
            engine.DocumentSettings.LoadCallback = (ref DocumentInfo info) =>
            {
                if (Path.GetExtension(info.Uri.AbsolutePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    info.Category = DocumentCategory.Json;
                }
            };

            engine.Execute(new DocumentInfo { Category = ModuleCategory.Standard }, "import result from 'JavaScript/Object.json'; globalThis.result = result;");
            var result = (ScriptObject)engine.Global.GetProperty("result");
            Assert.AreEqual(2, result.PropertyNames.Count());
            Assert.AreEqual(123, result.GetProperty("foo"));
            Assert.AreEqual("baz", result.GetProperty("bar"));

            engine.DocumentSettings.AddSystemDocument("ObjectWithFunction.json", DocumentCategory.Json, "{ \"foo\": 123, \"bar\": \"baz\", \"qux\": function(){} }");
            TestUtil.AssertException<ScriptEngineException>(() => engine.Execute(new DocumentInfo { Category = ModuleCategory.Standard }, "import result from 'ObjectWithFunction.json'; globalThis.result = result;"));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Json_Array()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading | DocumentAccessFlags.AllowCategoryMismatch;
            engine.DocumentSettings.LoadCallback = (ref DocumentInfo info) =>
            {
                if (Path.GetExtension(info.Uri.AbsolutePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    info.Category = DocumentCategory.Json;
                }
            };

            engine.Execute(new DocumentInfo { Category = ModuleCategory.Standard }, "import result from 'JavaScript/Array.json'; globalThis.result = result;");
            var result = (ScriptObject)engine.Global.GetProperty("result");
            Assert.AreEqual(4, result.PropertyIndices.Count());
            Assert.AreEqual(123, result.GetProperty(0));
            Assert.AreEqual("foo", result.GetProperty(1));
            Assert.AreEqual(4.56, result.GetProperty(2));
            Assert.AreEqual("bar", result.GetProperty(3));

            engine.DocumentSettings.AddSystemDocument("ArrayWithFunction.json", DocumentCategory.Json, "[ 123, \"foo\", 4.56, \"bar\", function(){} ]");
            TestUtil.AssertException<ScriptEngineException>(() => engine.Execute(new DocumentInfo { Category = ModuleCategory.Standard }, "import result from 'ArrayWithFunction.json'; globalThis.result = result;"));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_Standard_Json_Malformed()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading | DocumentAccessFlags.AllowCategoryMismatch;
            engine.DocumentSettings.LoadCallback = (ref DocumentInfo info) =>
            {
                if (Path.GetExtension(info.Uri.AbsolutePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    info.Category = DocumentCategory.Json;
                }
            };

            // ReSharper disable once AccessToDisposedClosure
            TestUtil.AssertException<ScriptEngineException>(() => engine.Execute(new DocumentInfo { Category = ModuleCategory.Standard }, "import result from 'JavaScript/Malformed.json'; globalThis.result = result;"));
        }

        [TestMethod, TestCategory("V8Module")]
        public async Task V8Module_Standard_TopLevelAwait_Result()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableTaskPromiseConversion);

            engine.AddHostType(typeof(Task));
            Assert.AreEqual(123, await (Task<object>)engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                await Task.Delay(500);
                import.meta.setResult(123);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public async Task V8Module_Standard_TopLevelAwait_Exception()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableTaskPromiseConversion);

            engine.AddHostType(typeof(Task));
            ScriptEngineException scriptEngineException = null;

            try
            {
                await (Task<object>)engine.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, @"
                    await Task.Delay(500);
                    await Task.BogusMethod();
                ");
            }
            catch (ScriptEngineException exception)
            {
                scriptEngineException = exception;
                Console.WriteLine(exception);
            }

            Assert.IsNotNull(scriptEngineException);
            Assert.AreEqual("TypeError: Task.BogusMethod is not a function", scriptEngineException.Message);
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_File()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(123 + 456, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Arithmetic = require('JavaScript/CommonJS/Arithmetic/Arithmetic');
                return Arithmetic.Add(123, 456);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_File_ForeignExtension()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(123 + 456, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Arithmetic = require('JavaScript/CommonJS/Arithmetic/Arithmetic.bogus');
                return Arithmetic.BogusAdd(123, 456);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_File_Nested()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('JavaScript/CommonJS/Geometry/Geometry');
                return new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_File_Disabled()
        {
            TestUtil.AssertException<UnauthorizedAccessException>(() => engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('JavaScript/CommonJS/Geometry/Geometry');
                return new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_File_PathlessImport()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "CommonJS", "Arithmetic"),
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "CommonJS", "Geometry")
            );

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(123 + 456, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Arithmetic = require('Arithmetic');
                return Arithmetic.Add(123, 456);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_File_PathlessImport_Nested()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "CommonJS", "Arithmetic"),
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "CommonJS", "Geometry")
            );

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('GeometryWithPathlessImport');
                return new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_File_PathlessImport_Disabled()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "CommonJS", "Arithmetic"),
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "CommonJS", "Geometry")
            );

            TestUtil.AssertException<UnauthorizedAccessException>(() => engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('Geometry');
                return new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Web()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            Assert.AreEqual(123 + 456, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Arithmetic = require('https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/CommonJS/Arithmetic/Arithmetic');
                return Arithmetic.Add(123, 456);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Web_Nested()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/CommonJS/Geometry/Geometry');
                return new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Web_Disabled()
        {
            TestUtil.AssertException<UnauthorizedAccessException>(() => engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/CommonJS/Geometry/Geometry');
                return new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Web_PathlessImport()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/CommonJS/Arithmetic",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/CommonJS/Geometry"
            );

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            Assert.AreEqual(123 + 456, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Arithmetic = require('Arithmetic');
                return Arithmetic.Add(123, 456);
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Web_PathlessImport_Nested()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/CommonJS/Arithmetic",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/CommonJS/Geometry"
            );

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableWebLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('GeometryWithPathlessImport');
                return new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Web_PathlessImport_Disabled()
        {
            engine.DocumentSettings.SearchPath = string.Join(";",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/CommonJS/Arithmetic",
                "https://raw.githubusercontent.com/microsoft/ClearScript/master/ClearScriptTest/JavaScript/CommonJS/Geometry"
            );

            TestUtil.AssertException<UnauthorizedAccessException>(() => engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('Geometry');
                return new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Compilation()
        {
            var module = engine.Compile(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('JavaScript/CommonJS/Geometry/Geometry');
                return new Geometry.Square(25).Area;
            ");

            using (module)
            {
                engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
                Assert.AreEqual(25 * 25, engine.Evaluate(module));

                // re-evaluating a module is a no-op
                Assert.IsInstanceOfType(engine.Evaluate(module), typeof(Undefined));
            }
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Compilation_CodeCache()
        {
            var code = @"
                let Geometry = require('JavaScript/CommonJS/Geometry/Geometry');
                return new Geometry.Square(25).Area;
            ";

            byte[] cacheBytes;
            using (engine.Compile(new DocumentInfo { Category = ModuleCategory.CommonJS }, code, V8CacheKind.Code, out cacheBytes))
            {
            }

            Assert.IsNotNull(cacheBytes);
            Assert.IsTrue(cacheBytes.Length > 350); // typical size is ~700

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDynamicModuleImports | V8ScriptEngineFlags.EnableDebugging);

            var module = engine.Compile(new DocumentInfo { Category = ModuleCategory.CommonJS }, code, V8CacheKind.Code, cacheBytes, out var cacheAccepted);
            Assert.IsTrue(cacheAccepted);

            using (module)
            {
                engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
                Assert.AreEqual(25 * 25, engine.Evaluate(module));

                // re-evaluating a module is a no-op
                Assert.IsInstanceOfType(engine.Evaluate(module), typeof(Undefined));
            }
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Compilation_Runtime_CodeCache()
        {
            var code = @"
                let Geometry = require('JavaScript/CommonJS/Geometry/Geometry');
                return new Geometry.Square(25).Area;
            ";

            byte[] cacheBytes;
            using (engine.Compile(new DocumentInfo { Category = ModuleCategory.CommonJS }, code, V8CacheKind.Code, out cacheBytes))
            {
            }

            Assert.IsNotNull(cacheBytes);
            Assert.IsTrue(cacheBytes.Length > 350); // typical size is ~700

            engine.Dispose();

            using (var runtime = new V8Runtime(V8RuntimeFlags.EnableDebugging))
            {
                engine = runtime.CreateScriptEngine();

                var module = runtime.Compile(new DocumentInfo { Category = ModuleCategory.CommonJS }, code, V8CacheKind.Code, cacheBytes, out var cacheAccepted);
                Assert.IsTrue(cacheAccepted);

                using (module)
                {
                    engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
                    Assert.AreEqual(25 * 25, engine.Evaluate(module));

                    // re-evaluating a module is a no-op
                    Assert.IsInstanceOfType(engine.Evaluate(module), typeof(Undefined));
                }
            }
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_SideEffects()
        {
            using (var runtime = new V8Runtime())
            {
                runtime.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableAllLoading;
                var module = runtime.CompileDocument("JavaScript/CommonJS/ModuleWithSideEffects.js", ModuleCategory.CommonJS);

                using (var testEngine = runtime.CreateScriptEngine())
                {
                    testEngine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableAllLoading;
                    testEngine.Execute("foo = {}");
                    Assert.AreEqual(625, testEngine.Evaluate(module));
                    Assert.AreEqual(625, testEngine.Evaluate("foo.bar"));

                    // re-evaluating a module is a no-op
                    Assert.IsInstanceOfType(testEngine.Evaluate(module), typeof(Undefined));
                }

                using (var testEngine = runtime.CreateScriptEngine())
                {
                    testEngine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableAllLoading;
                    testEngine.Execute("foo = {}");
                    Assert.AreEqual(625, testEngine.Evaluate(module));
                    Assert.AreEqual(625, testEngine.Evaluate("foo.bar"));

                    // re-evaluating a module is a no-op
                    Assert.IsInstanceOfType(testEngine.Evaluate(module), typeof(Undefined));
                }
            }
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Caching()
        {
            Assert.AreEqual(1UL, engine.GetRuntimeStatistics().ScriptCount);

            var info = new DocumentInfo { Category = ModuleCategory.CommonJS };

            Assert.AreEqual(Math.PI, engine.Evaluate(info, "return Math.PI"));
            Assert.AreEqual(Math.PI, engine.Evaluate(info, "return Math.PI"));
            Assert.AreEqual(4UL, engine.GetRuntimeStatistics().ScriptCount);

            info = new DocumentInfo("Test") { Category = ModuleCategory.CommonJS };

            Assert.AreEqual(Math.E, engine.Evaluate(info, "return Math.E"));
            Assert.IsInstanceOfType(engine.Evaluate(info, "return Math.E"), typeof(Undefined));
            Assert.AreEqual(5UL, engine.GetRuntimeStatistics().ScriptCount);

            Assert.AreEqual(Math.PI, engine.Evaluate(info, "return Math.PI"));
            Assert.IsInstanceOfType(engine.Evaluate(info, "return Math.PI"), typeof(Undefined));
            Assert.AreEqual(6UL, engine.GetRuntimeStatistics().ScriptCount);

            using (var runtime = new V8Runtime())
            {
                for (var i = 0; i < 10; i++)
                {
                    using (var testEngine = runtime.CreateScriptEngine())
                    {
                        Assert.AreEqual(Math.PI, testEngine.Evaluate(info, "return Math.PI"));
                        Assert.AreEqual(Math.E, testEngine.Evaluate(info, "return Math.E"));
                        Assert.AreEqual((i < 1) ? 4UL : 0UL, testEngine.GetStatistics().ScriptCount);
                    }
                }

                Assert.AreEqual(4UL, runtime.GetStatistics().ScriptCount);
            }

            using (var runtime = new V8Runtime())
            {
                for (var i = 0; i < 300; i++)
                {
                    using (var testEngine = runtime.CreateScriptEngine())
                    {
                        Assert.AreEqual(Math.PI, testEngine.Evaluate(info, "return Math.PI"));
                        Assert.IsInstanceOfType(testEngine.Evaluate(info, "return Math.PI"), typeof(Undefined));
                    }
                }

                Assert.AreEqual(3UL, runtime.GetStatistics().ScriptCount);
            }

            using (var runtime = new V8Runtime())
            {
                using (var testEngine = runtime.CreateScriptEngine())
                {
                    for (var i = 0; i < 300; i++)
                    {
                        Assert.AreEqual(Math.PI + i, testEngine.Evaluate(info, "return Math.PI" + "+" + i + ";"));
                    }

                    Assert.AreEqual(302UL, testEngine.GetStatistics().ScriptCount);
                    Assert.AreEqual(300, testEngine.GetStatistics().CommonJSModuleCacheSize);
                }

                Assert.AreEqual(302UL, runtime.GetStatistics().ScriptCount);
            }
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Module()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;

            dynamic first = engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                return require('JavaScript/CommonJS/Geometry/Geometry');
            ");

            Assert.IsInstanceOfType(first.Module.id, typeof(string));
            Assert.AreEqual(first.Module.id, first.Module.uri);

            dynamic second = engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                return require('" + (string)first.Module.id + @"');
            ");

            Assert.AreEqual(first.Module.id, second.Module.id);
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Context()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            engine.DocumentSettings.ContextCallback = CustomLoader.CreateDocumentContext;

            Assert.AreEqual(123, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('JavaScript/CommonJS/Geometry/Geometry');
                return Geometry.Meta.foo;
            "));

            Assert.AreEqual(456.789, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('JavaScript/CommonJS/Geometry/Geometry');
                return Geometry.Meta.bar;
            "));

            Assert.AreEqual("bogus", engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('JavaScript/CommonJS/Geometry/Geometry');
                return Geometry.Meta.baz;
            "));

            Assert.IsInstanceOfType(engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('JavaScript/CommonJS/Geometry/Geometry');
                return new Geometry.Meta.qux();
            "), typeof(Random));

            Assert.IsInstanceOfType(engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('JavaScript/CommonJS/Geometry/Geometry');
                return Geometry.Meta.quux;
            "), typeof(Undefined));

            Assert.AreEqual(Math.PI, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Arithmetic = require('JavaScript/CommonJS/Arithmetic/Arithmetic');
                return Arithmetic.Meta.foo;
            "));

            Assert.IsInstanceOfType(engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Arithmetic = require('JavaScript/CommonJS/Arithmetic/Arithmetic');
                return Arithmetic.Meta.bar;
            "), typeof(Undefined));

            engine.DocumentSettings.SearchPath = string.Join(";",
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "CommonJS", "Arithmetic"),
                Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "CommonJS", "Geometry")
            );

            TestUtil.AssertException<UnauthorizedAccessException>(() => engine.Execute(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('GeometryWithPathlessImport');
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_OverrideExports()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;

            Assert.AreEqual(Math.PI, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                return require('JavaScript/CommonJS/NewMath').PI;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_SystemDocument()
        {
            engine.DocumentSettings.AddSystemDocument("test", ModuleCategory.CommonJS, @"
                exports.Add = function (a, b) {
                    return a + b;
                }
            ");

            dynamic add = engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                return require('test').Add
            ");

            Assert.AreEqual(579, add(123, 456));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_CircularReference()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(25 * 25, engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, @"
                let Geometry = require('JavaScript/CommonJSWithCycles/Geometry/Geometry');
                return new Geometry.Square(25).Area;
            "));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Json_Object()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading | DocumentAccessFlags.AllowCategoryMismatch;
            engine.DocumentSettings.LoadCallback = (ref DocumentInfo info) =>
            {
                if (Path.GetExtension(info.Uri.AbsolutePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    info.Category = DocumentCategory.Json;
                }
            };

            var result = (ScriptObject)engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, "return require('JavaScript/Object.json')");
            Assert.AreEqual(2, result.PropertyNames.Count());
            Assert.AreEqual(123, result.GetProperty("foo"));
            Assert.AreEqual("baz", result.GetProperty("bar"));

            engine.DocumentSettings.AddSystemDocument("ObjectWithFunction.json", DocumentCategory.Json, "{ \"foo\": 123, \"bar\": \"baz\", \"qux\": function(){} }");
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, "return require('ObjectWithFunction.json')"));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Json_Array()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading | DocumentAccessFlags.AllowCategoryMismatch;
            engine.DocumentSettings.LoadCallback = (ref DocumentInfo info) =>
            {
                if (Path.GetExtension(info.Uri.AbsolutePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    info.Category = DocumentCategory.Json;
                }
            };

            var result = (ScriptObject)engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, "return require('JavaScript/Array.json')");
            Assert.AreEqual(4, result.PropertyIndices.Count());
            Assert.AreEqual(123, result.GetProperty(0));
            Assert.AreEqual("foo", result.GetProperty(1));
            Assert.AreEqual(4.56, result.GetProperty(2));
            Assert.AreEqual("bar", result.GetProperty(3));

            engine.DocumentSettings.AddSystemDocument("ArrayWithFunction.json", DocumentCategory.Json, "[ 123, \"foo\", 4.56, \"bar\", function(){} ]");
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, "return require('ArrayWithFunction.json')"));
        }

        [TestMethod, TestCategory("V8Module")]
        public void V8Module_CommonJS_Json_Malformed()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading | DocumentAccessFlags.AllowCategoryMismatch;
            engine.DocumentSettings.LoadCallback = (ref DocumentInfo info) =>
            {
                if (Path.GetExtension(info.Uri.AbsolutePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    info.Category = DocumentCategory.Json;
                }
            };

            // ReSharper disable once AccessToDisposedClosure
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate(new DocumentInfo { Category = ModuleCategory.CommonJS }, "return require('JavaScript/Malformed.json')"));
        }

        #endregion

        #region miscellaneous

        private sealed class CustomLoader : DocumentLoader
        {
            public override Task<Document> LoadDocumentAsync(DocumentSettings settings, DocumentInfo? sourceInfo, string specifier, DocumentCategory category, DocumentContextCallback contextCallback)
            {
                return Default.LoadDocumentAsync(settings, sourceInfo, specifier, category, contextCallback ?? CreateDocumentContext);
            }

            public static IDictionary<string, object> CreateDocumentContext(DocumentInfo info)
            {
                if (info.Uri is not null)
                {
                    var name = Path.GetFileName(info.Uri.AbsolutePath);

                    if (name.Equals("Geometry.js", StringComparison.OrdinalIgnoreCase))
                    {
                        return new Dictionary<string, object>
                        {
                            { "foo", 123 },
                            { "bar", 456.789 },
                            { "baz", "bogus" },
                            { "qux", typeof(Random).ToHostType() }
                        };
                    }

                    if (name.Equals("Arithmetic.js", StringComparison.OrdinalIgnoreCase))
                    {
                        return new Dictionary<string, object>
                        {
                            { "foo", Math.PI }
                        };
                    }

                    throw new UnauthorizedAccessException("Module context access is prohibited in this module");
                }

                return null;
            }
        }

        #endregion
    }
}
