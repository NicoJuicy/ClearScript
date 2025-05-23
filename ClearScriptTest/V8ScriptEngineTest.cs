// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.ClearScript.JavaScript;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.ClearScript.Util;
using Microsoft.ClearScript.V8;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Microsoft.ClearScript.Test
{
    // ReSharper disable once PartialTypeWithSinglePart

    [TestClass]
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable", Justification = "Test classes use TestCleanupAttribute for deterministic teardown.")]
    [SuppressMessage("ReSharper", "StringLiteralTypo", Justification = "Typos in test code are acceptable.")]
    public partial class V8ScriptEngineTest : ClearScriptTest
    {
        #region setup / teardown

        private V8ScriptEngine engine;

        [TestInitialize]
        public void TestInitialize()
        {
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            engine.Dispose();
            BaseTestCleanup();
        }

        #endregion

        #region test methods

        // ReSharper disable InconsistentNaming

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostObject()
        {
            var host = new HostFunctions();
            engine.AddHostObject("host", host);
            Assert.AreSame(host, engine.Evaluate("host"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        [ExpectedException(typeof(InvalidOperationException))]
        public void V8ScriptEngine_AddHostObject_Scalar()
        {
            engine.AddHostObject("value", 123);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostObject_Enum()
        {
            const DayOfWeek value = DayOfWeek.Wednesday;
            engine.AddHostObject("value", value);
            Assert.AreEqual(value, engine.Evaluate("value"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostObject_Struct()
        {
            var date = new DateTime(2007, 5, 22, 6, 15, 43);
            engine.AddHostObject("date", date);
            Assert.AreEqual(date, engine.Evaluate("date"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostObject_GlobalMembers()
        {
            var host = new HostFunctions();
            engine.AddHostObject("host", HostItemFlags.GlobalMembers, host);
            Assert.IsInstanceOfType(engine.Evaluate("newObj()"), typeof(PropertyBag));

            engine.AddHostObject("test", HostItemFlags.GlobalMembers, this);
            engine.Execute("TestProperty = newObj()");
            Assert.IsInstanceOfType(TestProperty, typeof(PropertyBag));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostObject_GlobalMembers_Overwrite()
        {
            const int fooFirst = 123;
            const int fooSecond = 456;
            const int barSecond = 789;
            engine.AddHostObject("bar", HostItemFlags.GlobalMembers, new { second = barSecond });
            engine.AddHostObject("foo", HostItemFlags.GlobalMembers, new { second = fooSecond });
            engine.AddHostObject("foo", HostItemFlags.GlobalMembers, new { first = fooFirst });
            Assert.AreEqual(fooFirst, engine.Evaluate("first"));
            Assert.AreEqual(barSecond, engine.Evaluate("second"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        [ExpectedException(typeof(ScriptEngineException))]
        public void V8ScriptEngine_AddHostObject_DefaultAccess()
        {
            engine.AddHostObject("test", this);
            engine.Execute("test.PrivateMethod()");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostObject_PrivateAccess()
        {
            engine.AddHostObject("test", HostItemFlags.PrivateAccess, this);
            engine.Execute("test.PrivateMethod()");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddRestrictedHostObject_BaseClass()
        {
            var host = new ExtendedHostFunctions() as HostFunctions;
            engine.AddRestrictedHostObject("host", host);
            Assert.IsInstanceOfType(engine.Evaluate("host.newObj()"), typeof(PropertyBag));
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("host.type('System.Int32')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddRestrictedHostObject_Interface()
        {
            const double value = 123.45;
            engine.AddRestrictedHostObject("convertible", value as IConvertible);
            engine.AddHostObject("culture", CultureInfo.InvariantCulture);
            Assert.AreEqual(value, engine.Evaluate("convertible.ToDouble(culture)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostType()
        {
            engine.AddHostObject("host", new HostFunctions());
            engine.AddHostType("Random", typeof(Random));
            Assert.IsInstanceOfType(engine.Evaluate("host.newObj(Random)"), typeof(Random));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostType_GlobalMembers()
        {
            engine.AddHostType("Guid", HostItemFlags.GlobalMembers, typeof(Guid));
            Assert.IsInstanceOfType(engine.Evaluate("NewGuid()"), typeof(Guid));

            engine.AddHostType("Test", HostItemFlags.GlobalMembers, GetType());
            engine.Execute("StaticTestProperty = NewGuid()");
            Assert.IsInstanceOfType(StaticTestProperty, typeof(Guid));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        [ExpectedException(typeof(ScriptEngineException))]
        public void V8ScriptEngine_AddHostType_DefaultAccess()
        {
            engine.AddHostType("Test", GetType());
            engine.Execute("Test.PrivateStaticMethod()");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostType_PrivateAccess()
        {
            engine.AddHostType("Test", HostItemFlags.PrivateAccess, GetType());
            engine.Execute("Test.PrivateStaticMethod()");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostType_Static()
        {
            engine.AddHostType("Enumerable", typeof(Enumerable));
            Assert.IsInstanceOfType(engine.Evaluate("Enumerable.Range(0, 5).ToArray()"), typeof(int[]));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostType_OpenGeneric()
        {
            engine.AddHostObject("host", new HostFunctions());
            engine.AddHostType("List", typeof(List<>));
            engine.AddHostType("Guid", typeof(Guid));
            Assert.IsInstanceOfType(engine.Evaluate("host.newObj(List(Guid))"), typeof(List<Guid>));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostType_ByName()
        {
            engine.AddHostObject("host", new HostFunctions());
            engine.AddHostType("Random", "System.Random");
            Assert.IsInstanceOfType(engine.Evaluate("host.newObj(Random)"), typeof(Random));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostType_ByNameWithAssembly()
        {
            engine.AddHostType("Enumerable", "System.Linq.Enumerable", "System.Core");
            Assert.IsInstanceOfType(engine.Evaluate("Enumerable.Range(0, 5).ToArray()"), typeof(int[]));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostType_ByNameWithTypeArgs()
        {
            engine.AddHostObject("host", new HostFunctions());
            engine.AddHostType("Dictionary", "System.Collections.Generic.Dictionary", typeof(string), typeof(int));
            Assert.IsInstanceOfType(engine.Evaluate("host.newObj(Dictionary)"), typeof(Dictionary<string, int>));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostType_DefaultName()
        {
            engine.AddHostType(typeof(Random));
            Assert.IsInstanceOfType(engine.Evaluate("new Random()"), typeof(Random));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostType_DefaultNameGeneric()
        {
            engine.AddHostType(typeof(List<int>));
            Assert.IsInstanceOfType(engine.Evaluate("new List()"), typeof(List<int>));

            engine.AddHostType(typeof(Dictionary<,>));
            engine.AddHostType(typeof(int));
            engine.AddHostType(typeof(double));
            Assert.IsInstanceOfType(engine.Evaluate("new Dictionary(Int32, Double, 100)"), typeof(Dictionary<int, double>));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AddHostTypes()
        {
            engine.AddHostTypes(typeof(Dictionary<,>), typeof(int), typeof(double));
            Assert.IsInstanceOfType(engine.Evaluate("new Dictionary(Int32, Double, 100)"), typeof(Dictionary<int, double>));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Evaluate()
        {
            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate("Math.E * Math.PI"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Evaluate_WithDocumentName()
        {
            const string documentName = "DoTheMath";
            engine.EnableDocumentNameTracking();
            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate(documentName, "Math.E * Math.PI"));
            Assert.IsFalse(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Evaluate_DiscardDocument()
        {
            const string documentName = "DoTheMath";
            engine.EnableDocumentNameTracking();
            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate(documentName, true, "Math.E * Math.PI"));
            Assert.IsFalse(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Evaluate_RetainDocument()
        {
            const string documentName = "DoTheMath";
            engine.EnableDocumentNameTracking();
            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate(documentName, false, "Math.E * Math.PI"));
            Assert.IsTrue(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Evaluate_DocumentInfo_WithDocumentName()
        {
            const string documentName = "DoTheMath";
            engine.EnableDocumentNameTracking();
            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate(new DocumentInfo(documentName), "Math.E * Math.PI"));
            Assert.IsFalse(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Evaluate_DocumentInfo_WithDocumentUri()
        {
            const string documentName = "DoTheMath";
            var documentUri = new Uri(@"c:\foo\bar\baz\" + documentName);
            engine.EnableDocumentNameTracking();
            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate(new DocumentInfo(documentUri) { Flags = DocumentFlags.None }, "Math.E * Math.PI"));
            Assert.IsTrue(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Evaluate_DocumentInfo_WithDocumentUri_Relative()
        {
            const string documentName = "DoTheMath";
            var documentUri = new Uri(documentName, UriKind.Relative);
            engine.EnableDocumentNameTracking();
            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate(new DocumentInfo(documentUri) { Flags = DocumentFlags.None }, "Math.E * Math.PI"));
            Assert.IsTrue(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Evaluate_DocumentInfo_DiscardDocument()
        {
            const string documentName = "DoTheMath";
            engine.EnableDocumentNameTracking();
            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate(new DocumentInfo(documentName) { Flags = DocumentFlags.IsTransient }, "Math.E * Math.PI"));
            Assert.IsFalse(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Evaluate_DocumentInfo_RetainDocument()
        {
            const string documentName = "DoTheMath";
            engine.EnableDocumentNameTracking();
            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate(new DocumentInfo(documentName) { Flags = DocumentFlags.None }, "Math.E * Math.PI"));
            Assert.IsTrue(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Execute()
        {
            engine.Execute("epi = Math.E * Math.PI");
            Assert.AreEqual(Math.E * Math.PI, engine.Script.epi);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Execute_WithDocumentName()
        {
            const string documentName = "DoTheMath";
            engine.EnableDocumentNameTracking();
            engine.Execute(documentName, "epi = Math.E * Math.PI");
            Assert.AreEqual(Math.E * Math.PI, engine.Script.epi);
            Assert.IsTrue(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Execute_DiscardDocument()
        {
            const string documentName = "DoTheMath";
            engine.EnableDocumentNameTracking();
            engine.Execute(documentName, true, "epi = Math.E * Math.PI");
            Assert.AreEqual(Math.E * Math.PI, engine.Script.epi);
            Assert.IsFalse(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Execute_RetainDocument()
        {
            const string documentName = "DoTheMath";
            engine.EnableDocumentNameTracking();
            engine.Execute(documentName, false, "epi = Math.E * Math.PI");
            Assert.AreEqual(Math.E * Math.PI, engine.Script.epi);
            Assert.IsTrue(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Execute_DocumentInfo_WithDocumentName()
        {
            const string documentName = "DoTheMath";
            engine.EnableDocumentNameTracking();
            engine.Execute(new DocumentInfo(documentName), "epi = Math.E * Math.PI");
            Assert.AreEqual(Math.E * Math.PI, engine.Script.epi);
            Assert.IsTrue(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Execute_DocumentInfo_WithDocumentUri()
        {
            const string documentName = "DoTheMath";
            var documentUri = new Uri(@"c:\foo\bar\baz\" + documentName);
            engine.EnableDocumentNameTracking();
            engine.Execute(new DocumentInfo(documentUri), "epi = Math.E * Math.PI");
            Assert.AreEqual(Math.E * Math.PI, engine.Script.epi);
            Assert.IsTrue(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Execute_DocumentInfo_WithDocumentUri_Relative()
        {
            const string documentName = "DoTheMath";
            var documentUri = new Uri(documentName, UriKind.Relative);
            engine.EnableDocumentNameTracking();
            engine.Execute(new DocumentInfo(documentUri), "epi = Math.E * Math.PI");
            Assert.AreEqual(Math.E * Math.PI, engine.Script.epi);
            Assert.IsTrue(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Execute_DocumentInfo_DiscardDocument()
        {
            const string documentName = "DoTheMath";
            engine.EnableDocumentNameTracking();
            engine.Execute(new DocumentInfo(documentName) { Flags = DocumentFlags.IsTransient }, "epi = Math.E * Math.PI");
            Assert.AreEqual(Math.E * Math.PI, engine.Script.epi);
            Assert.IsFalse(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Execute_DocumentInfo_RetainDocument()
        {
            const string documentName = "DoTheMath";
            engine.EnableDocumentNameTracking();
            engine.Execute(new DocumentInfo(documentName) { Flags = DocumentFlags.None }, "epi = Math.E * Math.PI");
            Assert.AreEqual(Math.E * Math.PI, engine.Script.epi);
            Assert.IsTrue(engine.GetDocumentNames().Any(name => name.StartsWith(documentName, StringComparison.Ordinal)));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Execute_CompiledScript()
        {
            using (var script = engine.Compile("epi = Math.E * Math.PI"))
            {
                engine.Execute(script);
                Assert.AreEqual(Math.E * Math.PI, engine.Script.epi);
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ExecuteCommand_EngineConvert()
        {
            Assert.AreEqual("[object Math]", engine.ExecuteCommand("Math"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ExecuteCommand_HostConvert()
        {
            var dateHostItem = HostItem.Wrap(engine, new DateTime(2007, 5, 22, 6, 15, 43));
            engine.AddHostObject("date", dateHostItem);
            Assert.AreEqual(dateHostItem.ToString(), engine.ExecuteCommand("date"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ExecuteCommand_var()
        {
            Assert.AreEqual("[undefined]", engine.ExecuteCommand("var x = 'foo'"));
            Assert.AreEqual("foo", engine.Script.x);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ExecuteCommand_HostVariable()
        {
            engine.Script.host = new HostFunctions();
            Assert.AreEqual("[HostVariable:String]", engine.ExecuteCommand("host.newVar('foo')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Invoke_ScriptFunction()
        {
            engine.Execute("function foo(x) { return x * Math.PI; }");
            Assert.AreEqual(Math.E * Math.PI, engine.Invoke("foo", Math.E));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Invoke_HostDelegate()
        {
            engine.Script.foo = new Func<double, double>(x => x * Math.PI);
            Assert.AreEqual(Math.E * Math.PI, engine.Invoke("foo", Math.E));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Interrupt()
        {
            var checkpoint = new ManualResetEvent(false);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                checkpoint.WaitOne();
                engine.Interrupt();
            });

            engine.AddHostObject("checkpoint", checkpoint);

            // V8 can't interrupt code that accesses only native data
            engine.AddHostObject("test", new { foo = "bar" });

            TestUtil.AssertException<OperationCanceledException>(() =>
            {
                try
                {
                    engine.Execute("checkpoint.Set(); while (true) { var foo = test.foo; }");
                }
                catch (ScriptInterruptedException exception)
                {
                    Assert.IsNull(exception.ScriptExceptionAsObject);
                    throw;
                }
            });

            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate("Math.E * Math.PI"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Interrupt_AwaitDebuggerAndPauseOnStart()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.AwaitDebuggerAndPauseOnStart);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(1000);
                engine.Interrupt();
            });

            TestUtil.AssertException<OperationCanceledException>(() =>
            {
                try
                {
                    engine.Evaluate("Math.E * Math.PI");
                }
                catch (ScriptInterruptedException exception)
                {
                    Assert.IsNull(exception.ScriptExceptionAsObject);
                    throw;
                }
            });

            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate("Math.E * Math.PI"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        [ExpectedException(typeof(ScriptEngineException))]
        public void V8ScriptEngine_AccessContext_Default()
        {
            engine.AddHostObject("test", this);
            engine.Execute("test.PrivateMethod()");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AccessContext_Private()
        {
            engine.AddHostObject("test", this);
            engine.AccessContext = GetType();
            engine.Execute("test.PrivateMethod()");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ContinuationCallback()
        {
            // V8 can't interrupt code that accesses only native data
            engine.AddHostObject("test", new { foo = "bar" });

            engine.ContinuationCallback = () => false;
            TestUtil.AssertException<OperationCanceledException>(() => engine.Execute("while (true) { var foo = test.foo; }"));
            engine.ContinuationCallback = null;
            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate("Math.E * Math.PI"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ContinuationCallback_AwaitDebuggerAndPauseOnStart()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.AwaitDebuggerAndPauseOnStart) { ContinuationCallback = () => false };

            TestUtil.AssertException<OperationCanceledException>(() => engine.Evaluate("Math.E * Math.PI"));
            engine.ContinuationCallback = null;
            Assert.AreEqual(Math.E * Math.PI, engine.Evaluate("Math.E * Math.PI"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_FileNameExtension()
        {
            Assert.AreEqual("js", engine.FileNameExtension);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Property()
        {
            var host = new HostFunctions();
            engine.Script.host = host;
            Assert.AreSame(host, engine.Script.host);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Property_Scalar()
        {
            const int value = 123;
            engine.Script.value = value;
            Assert.AreEqual(value, engine.Script.value);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Property_Enum()
        {
            const DayOfWeek value = DayOfWeek.Wednesday;
            engine.Script.value = value;
            Assert.AreEqual(value, engine.Script.value);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Property_Struct()
        {
            var date = new DateTime(2007, 5, 22, 6, 15, 43);
            engine.Script.date = date;
            Assert.AreEqual(date, engine.Script.date);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Index_ArrayItem()
        {
            const int index = 5;
            engine.Execute("foo = []");

            engine.Script.foo[index] = engine.Script.Math.PI;
            Assert.AreEqual(Math.PI, engine.Script.foo[index]);
            Assert.AreEqual(index + 1, engine.Evaluate("foo.length"));

            engine.Script.foo[index] = engine.Script.Math.E;
            Assert.AreEqual(Math.E, engine.Script.foo[index]);
            Assert.AreEqual(index + 1, engine.Evaluate("foo.length"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Index_Property()
        {
            const string name = "bar";
            engine.Execute("foo = {}");

            engine.Script.foo[name] = engine.Script.Math.PI;
            Assert.AreEqual(Math.PI, engine.Script.foo[name]);
            Assert.AreEqual(Math.PI, engine.Script.foo.bar);

            engine.Script.foo[name] = engine.Script.Math.E;
            Assert.AreEqual(Math.E, engine.Script.foo[name]);
            Assert.AreEqual(Math.E, engine.Script.foo.bar);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Method()
        {
            engine.Execute("function foo(x) { return x * x; }");
            Assert.AreEqual(25, engine.Script.foo(5));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Method_Intrinsic()
        {
            Assert.AreEqual(Math.E * Math.PI, engine.Script.eval("Math.E * Math.PI"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Property_VB()
        {
            TestUtil.InvokeVBTestSub(@"
                Using engine As New V8ScriptEngine
                    Dim host As New HostFunctions
                    engine.Script.host = host
                    Assert.AreSame(host, engine.Script.host)
                End Using
            ");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Property_Scalar_VB()
        {
            TestUtil.InvokeVBTestSub(@"
                Using engine As New V8ScriptEngine
                    Dim value = 123
                    engine.Script.value = value
                    Assert.AreEqual(value, engine.Script.value)
                End Using
            ");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Property_Enum_VB()
        {
            TestUtil.InvokeVBTestSub(@"
                Using engine As New V8ScriptEngine
                    Dim value = DayOfWeek.Wednesday
                    engine.Script.value = value
                    Assert.AreEqual(value, engine.Script.value)
                End Using
            ");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Property_Struct_VB()
        {
            TestUtil.InvokeVBTestSub(@"
                Using engine As New V8ScriptEngine
                    Dim value As New DateTime(2007, 5, 22, 6, 15, 43)
                    engine.Script.value = value
                    Assert.AreEqual(value, engine.Script.value)
                End Using
            ");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Index_ArrayItem_VB()
        {
            TestUtil.InvokeVBTestSub(@"
                Using engine As New V8ScriptEngine

                    Dim index = 5
                    engine.Execute(""foo = []"")

                    engine.Script.foo(index) = engine.Script.Math.PI
                    rem Assert.AreEqual(Math.PI, engine.Script.foo(index))
                    rem Assert.AreEqual(index + 1, engine.Evaluate(""foo.length""))

                    rem engine.Script.foo(index) = engine.Script.Math.E
                    rem Assert.AreEqual(Math.E, engine.Script.foo(index))
                    rem Assert.AreEqual(index + 1, engine.Evaluate(""foo.length""))

                End Using
            ");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Index_Property_VB()
        {
            TestUtil.InvokeVBTestSub(@"
                Using engine As New V8ScriptEngine

                    Dim name = ""bar""
                    engine.Execute(""foo = {}"")

                    engine.Script.foo(name) = engine.Script.Math.PI
                    Assert.AreEqual(Math.PI, engine.Script.foo(name))
                    Assert.AreEqual(Math.PI, engine.Script.foo.bar)

                    engine.Script.foo(name) = engine.Script.Math.E
                    Assert.AreEqual(Math.E, engine.Script.foo(name))
                    Assert.AreEqual(Math.E, engine.Script.foo.bar)

                End Using
            ");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Method_VB()
        {
            TestUtil.InvokeVBTestSub(@"
                Using engine As New V8ScriptEngine
                    engine.Execute(""function foo(x) { return x * x; }"")
                    Assert.AreEqual(25, engine.Script.foo(5))
                End Using
            ");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Script_Method_Intrinsic_VB()
        {
            TestUtil.InvokeVBTestSub(@"
                Using engine As New V8ScriptEngine
                    Assert.AreEqual(Math.E * Math.PI, engine.Script.eval(""Math.E * Math.PI""))
                End Using
            ");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_CollectGarbage()
        {
            engine.Execute("x = {}; for (i = 0; i < 1024 * 1024; i++) { x = { next: x }; }");
            var usedHeapSize = engine.GetRuntimeHeapInfo().UsedHeapSize;
            engine.CollectGarbage(true);
            Assert.IsTrue(usedHeapSize > engine.GetRuntimeHeapInfo().UsedHeapSize);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_CollectGarbage_HostObject()
        {
            // ReSharper disable RedundantAssignment

            WeakReference wr = null;

            new Action(() =>
            {
                var x = new object();
                wr = new WeakReference(x);
                engine.Script.x = x;

                x = null;
                engine.Script.x = null;
            })();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
            Assert.IsTrue(wr.IsAlive);

            engine.CollectGarbage(true);

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
            Assert.IsFalse(wr.IsAlive);

            // ReSharper restore RedundantAssignment
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Parallel()
        {
            engine.AddHostObject("host", new HostFunctions());
            engine.AddHostObject("clr", HostItemFlags.GlobalMembers, new HostTypeCollection("mscorlib"));

            const int threadCount = 256;
            engine.AddHostObject("list", Enumerable.Range(0, threadCount).ToList());
            Assert.AreEqual(threadCount, engine.Evaluate("list.Count"));

            var startEvent = new ManualResetEventSlim(false);
            var stopEvent = new ManualResetEventSlim(false);
            engine.AddHostObject("stopEvent", stopEvent);

            ThreadStart body = () =>
            {
                // ReSharper disable once AccessToDisposedClosure
                startEvent.Wait();

                engine.Execute("list.RemoveAt(0); if (list.Count == 0) { stopEvent.Set(); }");
            };

            var threads = Enumerable.Range(0, threadCount).Select(_ => new Thread(body)).ToArray();
            threads.ForEach(thread => thread.Start());

            startEvent.Set();
            stopEvent.Wait();
            Assert.AreEqual(0, engine.Evaluate("list.Count"));

            threads.ForEach(thread => thread.Join());
            startEvent.Dispose();
            stopEvent.Dispose();
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_new()
        {
            engine.AddHostObject("clr", HostItemFlags.GlobalMembers, new HostTypeCollection("mscorlib"));
            Assert.IsInstanceOfType(engine.Evaluate("new System.Random()"), typeof(Random));
            Assert.IsInstanceOfType(engine.Evaluate("new System.Random(100)"), typeof(Random));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_new_Generic()
        {
            engine.AddHostObject("clr", HostItemFlags.GlobalMembers, new HostTypeCollection("mscorlib"));
            Assert.IsInstanceOfType(engine.Evaluate("new System.Collections.Generic.Dictionary(System.Int32, System.String)"), typeof(Dictionary<int, string>));
            Assert.IsInstanceOfType(engine.Evaluate("new System.Collections.Generic.Dictionary(System.Int32, System.String, 100)"), typeof(Dictionary<int, string>));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_new_GenericNested()
        {
            engine.AddHostObject("clr", HostItemFlags.GlobalMembers, new HostTypeCollection("mscorlib", "System.Core"));
            engine.AddHostObject("dict", new Dictionary<int, string> { { 12345, "foo" }, { 54321, "bar" } });
            Assert.IsInstanceOfType(engine.Evaluate("vc = new (System.Collections.Generic.Dictionary(System.Int32, System.String).ValueCollection)(dict)"), typeof(Dictionary<int, string>.ValueCollection));
            Assert.IsTrue((bool)engine.Evaluate("vc.SequenceEqual(dict.Values)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_new_Scalar()
        {
            engine.AddHostObject("clr", HostItemFlags.GlobalMembers, new HostTypeCollection("mscorlib"));
            Assert.AreEqual(0, engine.Evaluate("new System.Int32"));
            Assert.AreEqual(0, engine.Evaluate("new System.Int32()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_new_Enum()
        {
            engine.AddHostObject("clr", HostItemFlags.GlobalMembers, new HostTypeCollection("mscorlib"));
            Assert.AreEqual(default(DayOfWeek), engine.Evaluate("new System.DayOfWeek"));
            Assert.AreEqual(default(DayOfWeek), engine.Evaluate("new System.DayOfWeek()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_new_Struct()
        {
            engine.AddHostObject("clr", HostItemFlags.GlobalMembers, new HostTypeCollection("mscorlib"));
            Assert.AreEqual(default(DateTime), engine.Evaluate("new System.DateTime"));
            Assert.AreEqual(default(DateTime), engine.Evaluate("new System.DateTime()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_new_NoMatch()
        {
            engine.AddHostObject("clr", HostItemFlags.GlobalMembers, new HostTypeCollection("mscorlib"));
            TestUtil.AssertException<MissingMemberException>(() => engine.Execute("new System.Random('a')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_General()
        {
            using (var console = new StringWriter())
            {
                var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                engine.AddHostObject("host", new ExtendedHostFunctions());
                engine.AddHostObject("clr", clr);

                engine.Execute(generalScript);
                Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_General_Precompiled()
        {
            using (var script = engine.Compile(generalScript))
            {
                using (var console = new StringWriter())
                {
                    var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                    clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                    engine.AddHostObject("host", new ExtendedHostFunctions());
                    engine.AddHostObject("clr", clr);

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));

                    console.GetStringBuilder().Clear();
                    Assert.AreEqual(string.Empty, console.ToString());

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_General_Precompiled_Dual()
        {
            engine.Dispose();
            using (var runtime = new V8Runtime())
            {
                using (var script = runtime.Compile(generalScript))
                {
                    engine = runtime.CreateScriptEngine();
                    using (var console = new StringWriter())
                    {
                        var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                        clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                        engine.AddHostObject("host", new ExtendedHostFunctions());
                        engine.AddHostObject("clr", clr);

                        engine.Evaluate(script);
                        Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));

                        console.GetStringBuilder().Clear();
                        Assert.AreEqual(string.Empty, console.ToString());

                        engine.Evaluate(script);
                        Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
                    }

                    engine.Dispose();
                    engine = runtime.CreateScriptEngine();
                    using (var console = new StringWriter())
                    {
                        var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                        clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                        engine.AddHostObject("host", new ExtendedHostFunctions());
                        engine.AddHostObject("clr", clr);

                        engine.Evaluate(script);
                        Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));

                        console.GetStringBuilder().Clear();
                        Assert.AreEqual(string.Empty, console.ToString());

                        engine.Evaluate(script);
                        Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
                    }
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_General_Precompiled_Execute()
        {
            using (var script = engine.Compile(generalScript))
            {
                using (var console = new StringWriter())
                {
                    var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                    clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                    engine.AddHostObject("host", new ExtendedHostFunctions());
                    engine.AddHostObject("clr", clr);

                    engine.Execute(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));

                    console.GetStringBuilder().Clear();
                    Assert.AreEqual(string.Empty, console.ToString());

                    engine.Execute(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_General_ParserCache()
        {
            #pragma warning disable CS0618 // Type or member is obsolete (V8CacheKind.Parser)

            engine.Dispose();
            engine = new V8ScriptEngine(); // default engine enables debugging, which disables caching (in older V8 versions)

            byte[] cacheBytes;
            using (var tempEngine = new V8ScriptEngine())
            {
                using (tempEngine.Compile(generalScript, V8CacheKind.Parser, out cacheBytes))
                {
                }
            }

            Assert.IsNotNull(cacheBytes);
            Assert.IsTrue(cacheBytes.Length > 2000); // typical size is ~4K

            using (var script = engine.Compile(generalScript, V8CacheKind.Parser, cacheBytes, out var cacheAccepted))
            {
                Assert.IsTrue(cacheAccepted);
                using (var console = new StringWriter())
                {
                    var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                    clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                    engine.AddHostObject("host", new ExtendedHostFunctions());
                    engine.AddHostObject("clr", clr);

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));

                    console.GetStringBuilder().Clear();
                    Assert.AreEqual(string.Empty, console.ToString());

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
                }
            }

            #pragma warning restore CS0618 // Type or member is obsolete (V8CacheKind.Parser)
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_General_ParserCache_BadData()
        {
            #pragma warning disable CS0618 // Type or member is obsolete (V8CacheKind.Parser)

            engine.Dispose();
            engine = new V8ScriptEngine(); // default engine enables debugging, which disables caching (in older V8 versions)

            byte[] cacheBytes;
            using (var tempEngine = new V8ScriptEngine())
            {
                using (tempEngine.Compile(generalScript, V8CacheKind.Parser, out cacheBytes))
                {
                }
            }

            Assert.IsNotNull(cacheBytes);
            Assert.IsTrue(cacheBytes.Length > 2000); // typical size is ~4K

            cacheBytes = cacheBytes.Take(cacheBytes.Length - 1).ToArray();

            using (var script = engine.Compile(generalScript, V8CacheKind.Parser, cacheBytes, out var cacheAccepted))
            {
                Assert.IsFalse(cacheAccepted);
                using (var console = new StringWriter())
                {
                    var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                    clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                    engine.AddHostObject("host", new ExtendedHostFunctions());
                    engine.AddHostObject("clr", clr);

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));

                    console.GetStringBuilder().Clear();
                    Assert.AreEqual(string.Empty, console.ToString());

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
                }
            }

            #pragma warning restore CS0618 // Type or member is obsolete (V8CacheKind.Parser)
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_General_ParserCache_DebuggingEnabled()
        {
            #pragma warning disable CS0618 // Type or member is obsolete (V8CacheKind.Parser)

            byte[] cacheBytes;
            using (var tempEngine = new V8ScriptEngine())
            {
                using (tempEngine.Compile(generalScript, V8CacheKind.Parser, out cacheBytes))
                {
                }
            }

            Assert.IsNotNull(cacheBytes);
            Assert.IsTrue(cacheBytes.Length > 2000); // typical size is ~4K

            using (var script = engine.Compile(generalScript, V8CacheKind.Parser, cacheBytes, out var cacheAccepted))
            {
                Assert.IsTrue(cacheAccepted);
                using (var console = new StringWriter())
                {
                    var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                    clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                    engine.AddHostObject("host", new ExtendedHostFunctions());
                    engine.AddHostObject("clr", clr);

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));

                    console.GetStringBuilder().Clear();
                    Assert.AreEqual(string.Empty, console.ToString());

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
                }
            }

            #pragma warning restore CS0618 // Type or member is obsolete (V8CacheKind.Parser)
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_General_CodeCache()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(); // default engine enables debugging, which disables caching (in older V8 versions)

            byte[] cacheBytes;
            using (var tempEngine = new V8ScriptEngine())
            {
                using (tempEngine.Compile(generalScript, V8CacheKind.Code, out cacheBytes))
                {
                }
            }

            Assert.IsNotNull(cacheBytes);
            Assert.IsTrue(cacheBytes.Length > 2000); // typical size is ~4K

            using (var script = engine.Compile(generalScript, V8CacheKind.Code, cacheBytes, out var cacheAccepted))
            {
                Assert.IsTrue(cacheAccepted);
                using (var console = new StringWriter())
                {
                    var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                    clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                    engine.AddHostObject("host", new ExtendedHostFunctions());
                    engine.AddHostObject("clr", clr);

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));

                    console.GetStringBuilder().Clear();
                    Assert.AreEqual(string.Empty, console.ToString());

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_General_CodeCache_BadData()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(); // default engine enables debugging, which disables caching (in older V8 versions)

            byte[] cacheBytes;
            using (var tempEngine = new V8ScriptEngine())
            {
                using (tempEngine.Compile(generalScript, V8CacheKind.Code, out cacheBytes))
                {
                }
            }

            Assert.IsNotNull(cacheBytes);
            Assert.IsTrue(cacheBytes.Length > 2000); // typical size is ~4K

            cacheBytes = cacheBytes.Take(cacheBytes.Length - 1).ToArray();

            using (var script = engine.Compile(generalScript, V8CacheKind.Code, cacheBytes, out var cacheAccepted))
            {
                Assert.IsFalse(cacheAccepted);
                using (var console = new StringWriter())
                {
                    var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                    clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                    engine.AddHostObject("host", new ExtendedHostFunctions());
                    engine.AddHostObject("clr", clr);

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));

                    console.GetStringBuilder().Clear();
                    Assert.AreEqual(string.Empty, console.ToString());

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_General_CodeCache_DebuggingEnabled()
        {
            byte[] cacheBytes;
            using (var tempEngine = new V8ScriptEngine())
            {
                using (tempEngine.Compile(generalScript, V8CacheKind.Code, out cacheBytes))
                {
                }
            }

            Assert.IsNotNull(cacheBytes);
            Assert.IsTrue(cacheBytes.Length > 2000); // typical size is ~4K

            using (var script = engine.Compile(generalScript, V8CacheKind.Code, cacheBytes, out var cacheAccepted))
            {
                Assert.IsTrue(cacheAccepted);
                using (var console = new StringWriter())
                {
                    var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                    clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                    engine.AddHostObject("host", new ExtendedHostFunctions());
                    engine.AddHostObject("clr", clr);

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));

                    console.GetStringBuilder().Clear();
                    Assert.AreEqual(string.Empty, console.ToString());

                    engine.Evaluate(script);
                    Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ErrorHandling_SyntaxError()
        {
            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.Execute("function foo() { int c; }");
                }
                catch (ScriptEngineException exception)
                {
                    if (exception.InnerException is ScriptEngineException innerScriptEngineException)
                    {
                        exception = innerScriptEngineException;
                    }

                    TestUtil.AssertValidException(engine, exception);
                    Assert.IsNotNull(exception.ScriptExceptionAsObject);
                    Assert.AreEqual("SyntaxError", exception.ScriptException.constructor.name);
                    Assert.IsNull(exception.InnerException);
                    Assert.IsTrue(exception.Message.Contains("SyntaxError"));
                    Assert.IsTrue(exception.ErrorDetails.Contains(" -> "));
                    throw;
                }
            });
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ErrorHandling_ThrowNonError()
        {
            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.Execute("(function () { throw 123; })()");
                }
                catch (ScriptEngineException exception)
                {
                    if (exception.InnerException is ScriptEngineException innerScriptEngineException)
                    {
                        exception = innerScriptEngineException;
                    }

                    TestUtil.AssertValidException(engine, exception);
                    Assert.AreEqual(123, exception.ScriptExceptionAsObject);
                    Assert.IsNull(exception.InnerException);
                    Assert.IsTrue(exception.Message.StartsWith("123", StringComparison.Ordinal));
                    Assert.IsTrue(exception.ErrorDetails.Contains(" -> "));
                    throw;
                }
            });
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ErrorHandling_ScriptError()
        {
            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.Execute("foo = {}; foo();");
                }
                catch (ScriptEngineException exception)
                {
                    if (exception.InnerException is ScriptEngineException innerScriptEngineException)
                    {
                        exception = innerScriptEngineException;
                    }

                    TestUtil.AssertValidException(engine, exception);
                    Assert.AreEqual("TypeError", exception.ScriptException.constructor.name);
                    Assert.IsNull(exception.InnerException);
                    throw;
                }
            });
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ErrorHandling_HostException()
        {
            engine.AddHostObject("host", new HostFunctions());

            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.Evaluate("host.proc(0)");
                }
                catch (ScriptEngineException exception)
                {
                    if (exception.InnerException is ScriptEngineException innerScriptEngineException)
                    {
                        exception = innerScriptEngineException;
                    }

                    TestUtil.AssertValidException(engine, exception);
                    Assert.AreEqual("Error", exception.ScriptException.constructor.name);
                    Assert.IsNotNull(exception.InnerException);

                    var hostException = exception.InnerException;
                    Assert.IsTrue((hostException is RuntimeBinderException) || (hostException is MissingMethodException));
                    TestUtil.AssertValidException(hostException);
                    Assert.IsNull(hostException.InnerException);

                    Assert.AreEqual("Error: " + hostException.Message, exception.Message);
                    throw;
                }
            });
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ErrorHandling_IgnoredHostException()
        {
            engine.AddHostObject("host", new HostFunctions());

            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.Execute("try { host.newObj(null); } catch(ex) {} foo = {}; foo();");
                }
                catch (ScriptEngineException exception)
                {
                    if (exception.InnerException is ScriptEngineException innerScriptEngineException)
                    {
                        exception = innerScriptEngineException;
                    }

                    TestUtil.AssertValidException(engine, exception);
                    Assert.AreEqual("TypeError", exception.ScriptException.constructor.name);
                    Assert.IsNull(exception.InnerException);
                    throw;
                }
            });
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ErrorHandling_NestedSyntaxError()
        {
            engine.AddHostObject("engine", engine);
            engine.Execute("good.js", "function bar() { engine.Execute('bad.js', 'function foo() { int c; }'); }");

            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.Script.bar();
                }
                catch (ScriptEngineException exception)
                {
                    if (exception.InnerException is ScriptEngineException innerScriptEngineException)
                    {
                        exception = innerScriptEngineException;
                    }

                    TestUtil.AssertValidException(engine, exception);
                    Assert.AreEqual("Error", exception.ScriptException.constructor.name);
                    Assert.IsNotNull(exception.InnerException);

                    var hostException = exception.InnerException;
                    Assert.IsInstanceOfType(hostException, typeof(TargetInvocationException));
                    TestUtil.AssertValidException(hostException);
                    Assert.IsNotNull(hostException.InnerException);

                    var nestedException = hostException.InnerException as ScriptEngineException;
                    if (nestedException?.InnerException is ScriptEngineException nestedInnerScriptEngineException)
                    {
                        nestedException = nestedInnerScriptEngineException;
                    }

                    Assert.IsNotNull(nestedException);
                    TestUtil.AssertValidException(engine, nestedException);
                    Assert.IsNull(nestedException.InnerException);
                    Assert.IsTrue(nestedException.ErrorDetails.Contains("at bad.js:1:22 -> "));
                    Assert.IsTrue(nestedException.ErrorDetails.Contains("at bar (good.js:1:25)"));

                    Assert.AreEqual("Error: " + hostException.GetBaseException().Message, exception.Message);
                    throw;
                }
            });
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ErrorHandling_NestedScriptError()
        {
            using (var innerEngine = new V8ScriptEngine("inner", V8ScriptEngineFlags.EnableDebugging))
            {
                engine.AddHostObject("engine", innerEngine);

                TestUtil.AssertException<ScriptEngineException>(() =>
                {
                    try
                    {
                        engine.Execute("engine.Execute('foo = {}; foo();')");
                    }
                    catch (ScriptEngineException exception)
                    {
                        if (exception.InnerException is ScriptEngineException innerScriptEngineException)
                        {
                            exception = innerScriptEngineException;
                        }

                        TestUtil.AssertValidException(engine, exception);
                        Assert.AreEqual("Error", exception.ScriptException.constructor.name);
                        Assert.IsNotNull(exception.InnerException);

                        var hostException = exception.InnerException;
                        Assert.IsInstanceOfType(hostException, typeof(TargetInvocationException));
                        TestUtil.AssertValidException(hostException);
                        Assert.IsNotNull(hostException.InnerException);

                        var nestedException = hostException.InnerException as ScriptEngineException;
                        if (nestedException?.InnerException is ScriptEngineException nestedInnerScriptEngineException)
                        {
                            nestedException = nestedInnerScriptEngineException;
                        }

                        Assert.IsNotNull(nestedException);

                        // ReSharper disable once AccessToDisposedClosure
                        TestUtil.AssertValidException(innerEngine, nestedException);

                        Assert.IsNull(nestedException.InnerException);

                        Assert.AreEqual("Error: " + hostException.GetBaseException().Message, exception.Message);
                        throw;
                    }
                });
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ErrorHandling_NestedHostException()
        {
            using (var innerEngine = new V8ScriptEngine("inner", V8ScriptEngineFlags.EnableDebugging))
            {
                innerEngine.AddHostObject("host", new HostFunctions());
                engine.AddHostObject("engine", innerEngine);

                TestUtil.AssertException<ScriptEngineException>(() =>
                {
                    try
                    {
                        engine.Execute("engine.Evaluate('host.proc(0)')");
                    }
                    catch (ScriptEngineException exception)
                    {
                        if (exception.InnerException is ScriptEngineException innerScriptEngineException)
                        {
                            exception = innerScriptEngineException;
                        }

                        TestUtil.AssertValidException(engine, exception);
                        Assert.AreEqual("Error", exception.ScriptException.constructor.name);
                        Assert.IsNotNull(exception.InnerException);

                        var hostException = exception.InnerException;
                        Assert.IsInstanceOfType(hostException, typeof(TargetInvocationException));
                        TestUtil.AssertValidException(hostException);
                        Assert.IsNotNull(hostException.InnerException);

                        var nestedException = hostException.InnerException as ScriptEngineException;
                        if (nestedException?.InnerException is ScriptEngineException nestedInnerScriptEngineException)
                        {
                            nestedException = nestedInnerScriptEngineException;
                        }

                        Assert.IsNotNull(nestedException);

                        // ReSharper disable once AccessToDisposedClosure
                        TestUtil.AssertValidException(innerEngine, nestedException);

                        Assert.IsNotNull(nestedException.InnerException);

                        var nestedHostException = nestedException.InnerException;
                        Assert.IsTrue((nestedHostException is RuntimeBinderException) || (nestedHostException is MissingMethodException));
                        TestUtil.AssertValidException(nestedHostException);
                        Assert.IsNull(nestedHostException.InnerException);

                        Assert.AreEqual("Error: " + nestedHostException.Message, nestedException.Message);
                        Assert.AreEqual("Error: " + hostException.GetBaseException().Message, exception.Message);
                        throw;
                    }
                });
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_MaxRuntimeHeapSize()
        {
            const int limit = 4 * 1024 * 1024;
            const string code = "x = {}; while (true) { x = { next: x }; }";

            engine.MaxRuntimeHeapSize = (UIntPtr)limit;

            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.Execute(code);
                }
                catch (ScriptEngineException exception)
                {
                    Assert.IsTrue(exception.IsFatal);
                    Assert.IsNull(exception.ScriptExceptionAsObject);
                    throw;
                }
            });

            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.CollectGarbage(true);
                    engine.Execute("x = 5");
                }
                catch (ScriptEngineException exception)
                {
                    Assert.IsTrue(exception.IsFatal);
                    Assert.IsNull(exception.ScriptExceptionAsObject);
                    throw;
                }
            });
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_MaxRuntimeHeapSize_Recovery()
        {
            const int limit = 4 * 1024 * 1024;
            const string code = "x = {}; while (true) { x = { next: x }; }";

            engine.MaxRuntimeHeapSize = (UIntPtr)limit;

            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.Execute(code);
                }
                catch (ScriptEngineException exception)
                {
                    Assert.IsTrue(exception.IsFatal);
                    Assert.IsNull(exception.ScriptExceptionAsObject);
                    throw;
                }
            });

            engine.MaxRuntimeHeapSize = (UIntPtr)(limit * 64);
            engine.Execute("x = 5");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_MaxRuntimeHeapSize_Dual()
        {
            const int limit = 4 * 1024 * 1024;
            const string code = "x = {}; for (i = 0; i < 16 * 1024 * 1024; i++) { x = { next: x }; }";

            engine.Execute(code);
            engine.CollectGarbage(true);
            var usedHeapSize = engine.GetRuntimeHeapInfo().UsedHeapSize;

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging) { MaxRuntimeHeapSize = (UIntPtr)limit };

            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.Execute(code);
                }
                catch (ScriptEngineException exception)
                {
                    Assert.IsTrue(exception.IsFatal);
                    Assert.IsNull(exception.ScriptExceptionAsObject);
                    throw;
                }
            });

            engine.CollectGarbage(true);
            Assert.IsTrue(usedHeapSize > engine.GetRuntimeHeapInfo().UsedHeapSize);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_MaxRuntimeHeapSize_ShortBursts()
        {
            const int limit = 4 * 1024 * 1024;
            const string code = "for (i = 0; i < 1024 * 1024; i++) { x = { next: x }; }";

            engine.MaxRuntimeHeapSize = (UIntPtr)limit;
            engine.RuntimeHeapSizeSampleInterval = TimeSpan.FromMilliseconds(30000);

            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.Execute("x = {}");
                    using (var script = engine.Compile(code))
                    {
                        while (true)
                        {
                            engine.Evaluate(script);
                        }
                    }
                }
                catch (ScriptEngineException exception)
                {
                    Assert.IsTrue(exception.IsFatal);
                    Assert.IsNull(exception.ScriptExceptionAsObject);
                    throw;
                }
            });
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_CreateInstance()
        {
            engine.Script.testObject = new DynamicTestObject();
            Assert.AreEqual("foo bar baz qux", engine.Evaluate("new testObject('foo', 'bar', 'baz', 'qux')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_CreateInstance_Fail()
        {
            engine.Script.testObject = new DynamicTestObject();
            TestUtil.AssertException<InvalidOperationException>(() => engine.Evaluate("new testObject()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_Invoke()
        {
            engine.Script.testObject = new DynamicTestObject();
            Assert.AreEqual("foo,bar,baz,qux", engine.Evaluate("testObject('foo', 'bar', 'baz', 'qux')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_Invoke_Fail()
        {
            engine.Script.testObject = new DynamicTestObject();
            TestUtil.AssertException<InvalidOperationException>(() => engine.Evaluate("testObject()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_InvokeMethod()
        {
            engine.Script.testObject = new DynamicTestObject();
            Assert.AreEqual("foo-bar-baz-qux", engine.Evaluate("testObject.DynamicMethod('foo', 'bar', 'baz', 'qux')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_InvokeMethod_Fail()
        {
            engine.Script.testObject = new DynamicTestObject();
            TestUtil.AssertException<MissingMemberException>(() => engine.Evaluate("testObject.DynamicMethod()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_InvokeMethod_FieldOverride()
        {
            engine.Script.testObject = new DynamicTestObject();
            Assert.AreEqual("foo.bar.baz.qux", engine.Evaluate("testObject.SomeField('foo', 'bar', 'baz', 'qux')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_InvokeMethod_FieldOverride_Fail()
        {
            engine.Script.testObject = new DynamicTestObject();
            TestUtil.AssertException<MissingMemberException>(() => engine.Evaluate("testObject.SomeField()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_InvokeMethod_PropertyOverride()
        {
            engine.Script.testObject = new DynamicTestObject();
            Assert.AreEqual("foo:bar:baz:qux", engine.Evaluate("testObject.SomeProperty('foo', 'bar', 'baz', 'qux')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_InvokeMethod_PropertyOverride_Fail()
        {
            engine.Script.testObject = new DynamicTestObject();
            TestUtil.AssertException<MissingMemberException>(() => engine.Evaluate("testObject.SomeProperty()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_InvokeMethod_DynamicOverload()
        {
            engine.Script.testObject = new DynamicTestObject();
            Assert.AreEqual("foo;bar;baz;qux", engine.Evaluate("testObject.SomeMethod('foo', 'bar', 'baz', 'qux')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_InvokeMethod_NonDynamicOverload()
        {
            engine.Script.testObject = new DynamicTestObject();
            Assert.AreEqual(Math.PI, engine.Evaluate("testObject.SomeMethod()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_InvokeMethod_NonDynamic()
        {
            engine.Script.testObject = new DynamicTestObject();
            Assert.AreEqual("Super Bass-O-Matic '76", engine.Evaluate("testObject.ToString()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_StaticType_Field()
        {
            engine.Script.testObject = new DynamicTestObject();
            engine.Script.host = new HostFunctions();
            Assert.IsInstanceOfType(engine.Evaluate("testObject.SomeField"), typeof(HostMethod));
            Assert.AreEqual(12345, engine.Evaluate("host.toStaticType(testObject).SomeField"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_StaticType_Property()
        {
            engine.Script.testObject = new DynamicTestObject();
            engine.Script.host = new HostFunctions();
            Assert.IsInstanceOfType(engine.Evaluate("testObject.SomeProperty"), typeof(HostMethod));
            Assert.AreEqual("Bogus", engine.Evaluate("host.toStaticType(testObject).SomeProperty"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_StaticType_Method()
        {
            engine.Script.testObject = new DynamicTestObject();
            engine.Script.host = new HostFunctions();
            Assert.AreEqual("bar+baz+qux", engine.Evaluate("host.toStaticType(testObject).SomeMethod('foo', 'bar', 'baz', 'qux')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_StaticType_Invoke()
        {
            engine.Script.testObject = new DynamicTestObject();
            engine.Script.host = new HostFunctions();
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("host.toStaticType(testObject)('foo', 'bar', 'baz', 'qux')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_Property()
        {
            engine.Script.testObject = new DynamicTestObject();
            Assert.IsInstanceOfType(engine.Evaluate("testObject.foo"), typeof(Undefined));
            Assert.AreEqual(123, engine.Evaluate("testObject.foo = 123"));
            Assert.AreEqual(123, engine.Evaluate("testObject.foo"));
            Assert.IsTrue((bool)engine.Evaluate("delete testObject.foo"));
            Assert.IsInstanceOfType(engine.Evaluate("testObject.foo"), typeof(Undefined));
            Assert.IsFalse((bool)engine.Evaluate("delete testObject.foo"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_Property_Fail()
        {
            engine.Script.testObject = new DynamicTestObject();
            Assert.IsInstanceOfType(engine.Evaluate("testObject.Zfoo"), typeof(Undefined));
            TestUtil.AssertException<MissingMemberException>(() => engine.Evaluate("testObject.Zfoo = 123"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_Property_Invoke()
        {
            engine.Script.testObject = new DynamicTestObject();
            Assert.IsInstanceOfType(engine.Evaluate("testObject.foo"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("testObject.foo = function (x) { return x.length; }"), typeof(DynamicObject));
            Assert.AreEqual("floccinaucinihilipilification".Length, engine.Evaluate("testObject.foo('floccinaucinihilipilification')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_Property_Invoke_Nested()
        {
            engine.Script.testObject = new DynamicTestObject();
            Assert.IsInstanceOfType(engine.Evaluate("testObject.foo"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("testObject.foo = testObject"), typeof(DynamicTestObject));
            Assert.AreEqual("foo,bar,baz,qux", engine.Evaluate("testObject.foo('foo', 'bar', 'baz', 'qux')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_Element()
        {
            engine.Script.testObject = new DynamicTestObject();
            engine.Script.host = new HostFunctions();
            Assert.IsInstanceOfType(engine.Evaluate("host.getElement(testObject, 1, 2, 3, 'foo')"), typeof(Undefined));
            Assert.AreEqual("bar", engine.Evaluate("host.setElement(testObject, 'bar', 1, 2, 3, 'foo')"));
            Assert.AreEqual("bar", engine.Evaluate("host.getElement(testObject, 1, 2, 3, 'foo')"));
            Assert.IsTrue((bool)engine.Evaluate("host.removeElement(testObject, 1, 2, 3, 'foo')"));
            Assert.IsInstanceOfType(engine.Evaluate("host.getElement(testObject, 1, 2, 3, 'foo')"), typeof(Undefined));
            Assert.IsFalse((bool)engine.Evaluate("host.removeElement(testObject, 1, 2, 3, 'foo')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_Element_Fail()
        {
            engine.Script.testObject = new DynamicTestObject();
            engine.Script.host = new HostFunctions();
            Assert.IsInstanceOfType(engine.Evaluate("host.getElement(testObject, 1, 2, 3, Math.PI)"), typeof(Undefined));
            TestUtil.AssertException<InvalidOperationException>(() => engine.Evaluate("host.setElement(testObject, 'bar', 1, 2, 3, Math.PI)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_Element_Index()
        {
            engine.Script.testObject = new DynamicTestObject { DisableInvocation = true, DisableDynamicMembers = true };
            engine.Script.host = new HostFunctions();

            Assert.IsInstanceOfType(engine.Evaluate("testObject[123]"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("host.getElement(testObject, 123)"), typeof(Undefined));
            Assert.AreEqual(456, engine.Evaluate("testObject[123] = 456"));
            Assert.AreEqual(456, engine.Evaluate("testObject[123]"));
            Assert.AreEqual(456, engine.Evaluate("host.getElement(testObject, 123)"));
            Assert.IsTrue((bool)engine.Evaluate("delete testObject[123]"));
            Assert.IsInstanceOfType(engine.Evaluate("testObject[123]"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("host.getElement(testObject, 123)"), typeof(Undefined));

            Assert.IsInstanceOfType(engine.Evaluate("testObject['foo']"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("host.getElement(testObject, 'foo')"), typeof(Undefined));
            Assert.AreEqual("bar", engine.Evaluate("testObject['foo'] = 'bar'"));
            Assert.AreEqual("bar", engine.Evaluate("testObject['foo']"));
            Assert.AreEqual("bar", engine.Evaluate("host.getElement(testObject, 'foo')"));
            Assert.IsTrue((bool)engine.Evaluate("delete testObject['foo']"));
            Assert.IsInstanceOfType(engine.Evaluate("testObject['foo']"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("host.getElement(testObject, 'foo')"), typeof(Undefined));

            Assert.IsInstanceOfType(engine.Evaluate("testObject('foo', 'bar', 'baz')"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("host.getElement(testObject, 'foo', 'bar', 'baz')"), typeof(Undefined));
            Assert.AreEqual("qux", engine.Evaluate("host.setElement(testObject, 'qux', 'foo', 'bar', 'baz')"));
            Assert.AreEqual("qux", engine.Evaluate("testObject('foo', 'bar', 'baz')"));
            Assert.AreEqual("qux", engine.Evaluate("host.getElement(testObject, 'foo', 'bar', 'baz')"));
            Assert.IsInstanceOfType(engine.Evaluate("host.setElement(testObject, undefined, 'foo', 'bar', 'baz')"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("testObject('foo', 'bar', 'baz')"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("host.getElement(testObject, 'foo', 'bar', 'baz')"), typeof(Undefined));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DynamicHostObject_Convert()
        {
            engine.Script.testObject = new DynamicTestObject();
            engine.Script.host = new HostFunctions();
            engine.AddHostType("int_t", typeof(int));
            engine.AddHostType("string_t", typeof(string));
            Assert.AreEqual(98765, engine.Evaluate("host.cast(int_t, testObject)"));
            Assert.AreEqual("Booyakasha!", engine.Evaluate("host.cast(string_t, testObject)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_HostIndexers()
        {
            engine.Script.testObject = new TestObject();

            TestUtil.AssertException<KeyNotFoundException>(() => engine.Evaluate("testObject.Item(123)"));
            TestUtil.AssertException<KeyNotFoundException>(() => engine.Evaluate("testObject.Item.get(123)"));
            Assert.AreEqual(Math.E, engine.Evaluate("testObject.Item.set(123, Math.E)"));
            Assert.AreEqual(Math.E, engine.Evaluate("testObject.Item.get(123)"));

            TestUtil.AssertException<KeyNotFoundException>(() => engine.Evaluate("testObject.Item('456')"));
            TestUtil.AssertException<KeyNotFoundException>(() => engine.Evaluate("testObject.Item.get('456')"));
            Assert.AreEqual(Math.Sqrt(3), engine.Evaluate("testObject.Item.set('456', Math.sqrt(3))"));
            Assert.AreEqual(Math.Sqrt(3), engine.Evaluate("testObject.Item.get('456')"));

            TestUtil.AssertException<KeyNotFoundException>(() => engine.Evaluate("testObject.Item(123, '456', 789.987, -0.12345)"));
            TestUtil.AssertException<KeyNotFoundException>(() => engine.Evaluate("testObject.Item.get(123, '456', 789.987, -0.12345)"));
            Assert.AreEqual(Math.Sqrt(7), engine.Evaluate("testObject.Item.set(123, '456', 789.987, -0.12345, Math.sqrt(7))"));
            Assert.AreEqual(Math.Sqrt(7), engine.Evaluate("testObject.Item.get(123, '456', 789.987, -0.12345)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_FormatCode()
        {
            try
            {
                engine.Execute("a", "\n\n\n     x = 3.a");
            }
            catch (ScriptEngineException exception)
            {
                Assert.IsTrue(exception.ErrorDetails.Contains(" a:4:10 "));
            }

            engine.FormatCode = true;
            try
            {
                engine.Execute("b", "\n\n\n     x = 3.a");
            }
            catch (ScriptEngineException exception)
            {
                Assert.IsTrue(exception.ErrorDetails.Contains(" b:1:5 "));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_GetStackTrace()
        {
            engine.AddHostObject("qux", new Func<object>(() => engine.GetStackTrace()));
            engine.Execute(@"
                function baz() { return qux(); }
                function bar() { return baz(); }
                function foo() { return bar(); }
            ");

            Assert.IsTrue(((string)engine.Evaluate("foo()")).EndsWith("    at baz (Script:2:41)\n    at bar (Script:3:41)\n    at foo (Script:4:41)\n    at Script [2] [temp]:1:1", StringComparison.Ordinal));
            Assert.IsTrue(((string)engine.Script.foo()).EndsWith("    at baz (Script:2:41)\n    at bar (Script:3:41)\n    at foo (Script:4:41)", StringComparison.Ordinal));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_MaxRuntimeHeapSize_Plumbing()
        {
            using (var runtime = new V8Runtime())
            {
                using (var engine1 = runtime.CreateScriptEngine())
                {
                    using (var engine2 = runtime.CreateScriptEngine())
                    {
                        var value = (UIntPtr)123456;
                        engine1.MaxRuntimeHeapSize = value;
                        Assert.AreEqual(value, engine1.MaxRuntimeHeapSize);
                        Assert.AreEqual(value, engine2.MaxRuntimeHeapSize);
                        Assert.AreEqual(value, runtime.MaxHeapSize);
                        Assert.AreEqual(UIntPtr.Zero, engine1.MaxRuntimeStackUsage);
                        Assert.AreEqual(UIntPtr.Zero, engine2.MaxRuntimeStackUsage);
                        Assert.AreEqual(UIntPtr.Zero, runtime.MaxStackUsage);

                        // ReSharper disable once RedundantCast
                        value = (UIntPtr)654321;
                        runtime.MaxHeapSize = value;
                        Assert.AreEqual(value, engine1.MaxRuntimeHeapSize);
                        Assert.AreEqual(value, engine2.MaxRuntimeHeapSize);
                        Assert.AreEqual(value, runtime.MaxHeapSize);
                        Assert.AreEqual(UIntPtr.Zero, engine1.MaxRuntimeStackUsage);
                        Assert.AreEqual(UIntPtr.Zero, engine2.MaxRuntimeStackUsage);
                        Assert.AreEqual(UIntPtr.Zero, runtime.MaxStackUsage);
                    }
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_RuntimeHeapSizeSampleInterval_Plumbing()
        {
            using (var runtime = new V8Runtime())
            {
                using (var engine1 = runtime.CreateScriptEngine())
                {
                    using (var engine2 = runtime.CreateScriptEngine())
                    {
                        var value = TimeSpan.FromMilliseconds(123456789.0);
                        engine1.RuntimeHeapSizeSampleInterval = value;
                        Assert.AreEqual(value, engine1.RuntimeHeapSizeSampleInterval);
                        Assert.AreEqual(value, engine2.RuntimeHeapSizeSampleInterval);
                        Assert.AreEqual(value, runtime.HeapSizeSampleInterval);

                        value = TimeSpan.FromMilliseconds(987654321.0);
                        runtime.HeapSizeSampleInterval = value;
                        Assert.AreEqual(value, engine1.RuntimeHeapSizeSampleInterval);
                        Assert.AreEqual(value, engine2.RuntimeHeapSizeSampleInterval);
                        Assert.AreEqual(value, runtime.HeapSizeSampleInterval);
                    }
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_MaxRuntimeStackUsage_Plumbing()
        {
            using (var runtime = new V8Runtime())
            {
                using (var engine1 = runtime.CreateScriptEngine())
                {
                    using (var engine2 = runtime.CreateScriptEngine())
                    {
                        var value = (UIntPtr)123456;
                        engine1.MaxRuntimeStackUsage = value;
                        Assert.AreEqual(value, engine1.MaxRuntimeStackUsage);
                        Assert.AreEqual(value, engine2.MaxRuntimeStackUsage);
                        Assert.AreEqual(value, runtime.MaxStackUsage);
                        Assert.AreEqual(UIntPtr.Zero, engine1.MaxRuntimeHeapSize);
                        Assert.AreEqual(UIntPtr.Zero, engine2.MaxRuntimeHeapSize);
                        Assert.AreEqual(UIntPtr.Zero, runtime.MaxHeapSize);

                        // ReSharper disable once RedundantCast
                        value = (UIntPtr)654321;
                        runtime.MaxStackUsage = value;
                        Assert.AreEqual(value, engine1.MaxRuntimeStackUsage);
                        Assert.AreEqual(value, engine2.MaxRuntimeStackUsage);
                        Assert.AreEqual(value, runtime.MaxStackUsage);
                        Assert.AreEqual(UIntPtr.Zero, engine1.MaxRuntimeHeapSize);
                        Assert.AreEqual(UIntPtr.Zero, engine2.MaxRuntimeHeapSize);
                        Assert.AreEqual(UIntPtr.Zero, runtime.MaxHeapSize);
                    }
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_MaxRuntimeStackUsage_ScriptOnly()
        {
            engine.MaxRuntimeStackUsage = (UIntPtr)(16 * 1024);
            TestUtil.AssertException<ScriptEngineException>(() => engine.Execute("(function () { arguments.callee(); })()"), false);
            Assert.AreEqual(Math.PI, engine.Evaluate("Math.PI"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_MaxRuntimeStackUsage_HostBounce()
        {
            engine.MaxRuntimeStackUsage = (UIntPtr)(32 * 1024);
            dynamic foo = engine.Evaluate("(function () { arguments.callee(); })");
            engine.Script.bar = new Action(() => foo());
            TestUtil.AssertException<ScriptEngineException>(() => engine.Execute("bar()"), false);
            Assert.AreEqual(Math.PI, engine.Evaluate("Math.PI"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_MaxRuntimeStackUsage_Alternating()
        {
            engine.MaxRuntimeStackUsage = (UIntPtr)(32 * 1024);
            dynamic foo = engine.Evaluate("(function () { bar(); })");
            engine.Script.bar = new Action(() => foo());
            TestUtil.AssertException<ScriptEngineException>(() => foo(), false);
            Assert.AreEqual(Math.PI, engine.Evaluate("Math.PI"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_MaxRuntimeStackUsage_Expansion()
        {
            engine.MaxRuntimeStackUsage = (UIntPtr)(32 * 1024);
            TestUtil.AssertException<ScriptEngineException>(() => engine.Execute("count = 0; (function () { count++; arguments.callee(); })()"), false);
            var count1 = engine.Script.count;
            engine.MaxRuntimeStackUsage = (UIntPtr)(64 * 1024);
            TestUtil.AssertException<ScriptEngineException>(() => engine.Execute("count = 0; (function () { count++; arguments.callee(); })()"), false);
            var count2 = engine.Script.count;
            Assert.IsTrue(count2 >= (count1 * 2));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_EnableAutoHostVariables()
        {
            const string pre = "123";
            var value = "foo";
            const int post = 456;

            engine.Execute("function foo(a, x, b) { var y = x; x = a + 'bar' + b; return y; }");
            Assert.AreEqual("foo", engine.Script.foo(pre, ref value, post));
            Assert.AreEqual("foo", value);  // JavaScript doesn't support output parameters

            engine.EnableAutoHostVariables = true;
            engine.Execute("function foo(a, x, b) { var y = x.value; x.value = a + 'bar' + b; return y; }");
            Assert.AreEqual("foo", engine.Script.foo(pre, ref value, post));
            Assert.AreEqual("123bar456", value);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_EnableAutoHostVariables_Delegate()
        {
            const string pre = "123";
            var value = "foo";
            const int post = 456;

            engine.Execute("function foo(a, x, b) { var y = x; x = a + 'bar' + b; return y; }");
            var del = DelegateFactory.CreateDelegate<TestDelegate>(engine, engine.Evaluate("foo"));
            Assert.AreEqual("foo", del(pre, ref value, post));
            Assert.AreEqual("foo", value);  // JavaScript doesn't support output parameters

            engine.EnableAutoHostVariables = true;
            engine.Execute("function foo(a, x, b) { var y = x.value; x.value = a + 'bar' + b; return y; }");
            del = DelegateFactory.CreateDelegate<TestDelegate>(engine, engine.Evaluate("foo"));
            Assert.AreEqual("foo", del(pre, ref value, post));
            Assert.AreEqual("123bar456", value);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ExceptionMarshaling()
        {
            Exception exception = new IOException("something awful happened");
            engine.AddRestrictedHostObject("exception", exception);

            engine.Script.foo = new Action(() => throw exception);

            engine.Execute(@"
                function bar() {
                    try {
                        foo();
                        return false;
                    }
                    catch (ex) {
                        return ex.hostException.GetBaseException() === exception;
                    }
                }
            ");

            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("bar()")));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ExceptionMarshaling_Suppression()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.HideHostExceptions);

            Exception exception = new IOException("something awful happened");
            engine.AddRestrictedHostObject("exception", exception);

            engine.Script.foo = new Action(() => throw exception);

            engine.Execute(@"
                function bar() {
                    try {
                        foo();
                        return false;
                    }
                    catch (ex) {
                        return typeof ex.hostException === 'undefined';
                    }
                }
            ");

            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("bar()")));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Current()
        {
            using (var innerEngine = new V8ScriptEngine())
            {
                engine.Script.test = new Action(() =>
                {
                    // ReSharper disable AccessToDisposedClosure

                    innerEngine.Script.test = new Action(() => Assert.AreSame(innerEngine, ScriptEngine.Current));
                    Assert.AreSame(engine, ScriptEngine.Current);
                    innerEngine.Execute("test()");
                    innerEngine.Script.test();
                    Assert.AreSame(engine, ScriptEngine.Current);

                    // ReSharper restore AccessToDisposedClosure
                });

                Assert.IsNull(ScriptEngine.Current);
                engine.Execute("test()");
                engine.Script.test();
                Assert.IsNull(ScriptEngine.Current);
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_EnableNullResultWrapping()
        {
            var testValue = new[] { 1, 2, 3, 4, 5 };
            engine.Script.host = new HostFunctions();
            engine.Script.foo = new NullResultWrappingTestObject<int[]>(testValue);

            Assert.IsFalse(Convert.ToBoolean(engine.Evaluate("foo.Value === null")));
            Assert.IsFalse(Convert.ToBoolean(engine.Evaluate("host.isNull(foo.Value)")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("foo.NullValue === null")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("host.isNull(foo.NullValue)")));
            Assert.IsFalse(Convert.ToBoolean(engine.Evaluate("foo.WrappedNullValue === null")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("host.isNull(foo.WrappedNullValue)")));

            Assert.AreSame(testValue, engine.Evaluate("foo.Method(foo.Value)"));
            Assert.IsNull(engine.Evaluate("foo.Method(foo.WrappedNullValue)"));
            TestUtil.AssertException<RuntimeBinderException, AmbiguousMatchException>(() => engine.Evaluate("foo.Method(foo.NullValue)"));

            engine.EnableNullResultWrapping = true;
            Assert.AreSame(testValue, engine.Evaluate("foo.Method(foo.Value)"));
            Assert.IsNull(engine.Evaluate("foo.Method(foo.WrappedNullValue)"));
            Assert.IsNull(engine.Evaluate("foo.Method(foo.NullValue)"));

            engine.EnableNullResultWrapping = false;
            Assert.AreSame(testValue, engine.Evaluate("foo.Method(foo.Value)"));
            Assert.IsNull(engine.Evaluate("foo.Method(foo.WrappedNullValue)"));
            TestUtil.AssertException<RuntimeBinderException, AmbiguousMatchException>(() => engine.Evaluate("foo.Method(foo.NullValue)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_EnableNullResultWrapping_String()
        {
            const string testValue = "bar";
            engine.Script.host = new HostFunctions();
            engine.Script.foo = new NullResultWrappingTestObject<string>(testValue);

            Assert.IsFalse(Convert.ToBoolean(engine.Evaluate("foo.Value === null")));
            Assert.IsFalse(Convert.ToBoolean(engine.Evaluate("host.isNull(foo.Value)")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("foo.NullValue === null")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("host.isNull(foo.NullValue)")));
            Assert.IsFalse(Convert.ToBoolean(engine.Evaluate("foo.WrappedNullValue === null")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("host.isNull(foo.WrappedNullValue)")));

            Assert.AreEqual(testValue, engine.Evaluate("foo.Method(foo.Value)"));
            Assert.IsNull(engine.Evaluate("foo.Method(foo.WrappedNullValue)"));
            TestUtil.AssertException<RuntimeBinderException, AmbiguousMatchException>(() => engine.Evaluate("foo.Method(foo.NullValue)"));

            engine.EnableNullResultWrapping = true;
            Assert.AreEqual(testValue, engine.Evaluate("foo.Method(foo.Value)"));
            Assert.IsNull(engine.Evaluate("foo.Method(foo.WrappedNullValue)"));
            Assert.IsNull(engine.Evaluate("foo.Method(foo.NullValue)"));

            engine.EnableNullResultWrapping = false;
            Assert.AreEqual(testValue, engine.Evaluate("foo.Method(foo.Value)"));
            Assert.IsNull(engine.Evaluate("foo.Method(foo.WrappedNullValue)"));
            TestUtil.AssertException<RuntimeBinderException, AmbiguousMatchException>(() => engine.Evaluate("foo.Method(foo.NullValue)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_EnableNullResultWrapping_Nullable()
        {
            int? testValue = 12345;
            engine.Script.host = new HostFunctions();
            engine.Script.foo = new NullResultWrappingTestObject<int?>(testValue);

            Assert.IsFalse(Convert.ToBoolean(engine.Evaluate("foo.Value === null")));
            Assert.IsFalse(Convert.ToBoolean(engine.Evaluate("host.isNull(foo.Value)")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("foo.NullValue === null")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("host.isNull(foo.NullValue)")));
            Assert.IsFalse(Convert.ToBoolean(engine.Evaluate("foo.WrappedNullValue === null")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("host.isNull(foo.WrappedNullValue)")));

            Assert.AreEqual(testValue, engine.Evaluate("foo.Method(foo.Value)"));
            Assert.IsNull(engine.Evaluate("foo.Method(foo.WrappedNullValue)"));
            TestUtil.AssertException<RuntimeBinderException, AmbiguousMatchException>(() => engine.Evaluate("foo.Method(foo.NullValue)"));

            engine.EnableNullResultWrapping = true;
            Assert.AreEqual(testValue, engine.Evaluate("foo.Method(foo.Value)"));
            Assert.IsNull(engine.Evaluate("foo.Method(foo.WrappedNullValue)"));
            Assert.IsNull(engine.Evaluate("foo.Method(foo.NullValue)"));

            engine.EnableNullResultWrapping = false;
            Assert.AreEqual(testValue, engine.Evaluate("foo.Method(foo.Value)"));
            Assert.IsNull(engine.Evaluate("foo.Method(foo.WrappedNullValue)"));
            TestUtil.AssertException<RuntimeBinderException, AmbiguousMatchException>(() => engine.Evaluate("foo.Method(foo.NullValue)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DefaultProperty()
        {
            engine.Script.foo = new DefaultPropertyTestObject();
            engine.AddHostType("DayOfWeek", typeof(DayOfWeek));

            engine.Execute("foo.Item.set('ghi', 321)");
            Assert.AreEqual(321, engine.Evaluate("foo('ghi')"));
            Assert.AreEqual(321, engine.Evaluate("foo.Item('ghi')"));
            Assert.AreEqual(321, engine.Evaluate("foo.Item.get('ghi')"));
            Assert.IsNull(engine.Evaluate("foo('jkl')"));

            engine.Execute("foo.Item.set(DayOfWeek.Saturday, -123)");
            Assert.AreEqual(-123, engine.Evaluate("foo(DayOfWeek.Saturday)"));
            Assert.AreEqual(-123, engine.Evaluate("foo.Item(DayOfWeek.Saturday)"));
            Assert.AreEqual(-123, engine.Evaluate("foo.Item.get(DayOfWeek.Saturday)"));
            Assert.IsNull(engine.Evaluate("foo(DayOfWeek.Sunday)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DefaultProperty_FieldTunneling()
        {
            engine.Script.foo = new DefaultPropertyTestContainer();
            engine.AddHostType("DayOfWeek", typeof(DayOfWeek));

            engine.Execute("foo.Field.Item.set('ghi', 321)");
            Assert.AreEqual(321, engine.Evaluate("foo.Field('ghi')"));
            Assert.AreEqual(321, engine.Evaluate("foo.Field.Item('ghi')"));
            Assert.AreEqual(321, engine.Evaluate("foo.Field.Item.get('ghi')"));
            Assert.IsNull(engine.Evaluate("foo.Field('jkl')"));

            engine.Execute("foo.Field.Item.set(DayOfWeek.Saturday, -123)");
            Assert.AreEqual(-123, engine.Evaluate("foo.Field(DayOfWeek.Saturday)"));
            Assert.AreEqual(-123, engine.Evaluate("foo.Field.Item(DayOfWeek.Saturday)"));
            Assert.AreEqual(-123, engine.Evaluate("foo.Field.Item.get(DayOfWeek.Saturday)"));
            Assert.IsNull(engine.Evaluate("foo.Field(DayOfWeek.Sunday)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DefaultProperty_PropertyTunneling()
        {
            engine.Script.foo = new DefaultPropertyTestContainer();
            engine.AddHostType("DayOfWeek", typeof(DayOfWeek));

            engine.Execute("foo.Property.Item.set('ghi', 321)");
            Assert.AreEqual(321, engine.Evaluate("foo.Property('ghi')"));
            Assert.AreEqual(321, engine.Evaluate("foo.Property.Item('ghi')"));
            Assert.AreEqual(321, engine.Evaluate("foo.Property.Item.get('ghi')"));
            Assert.IsNull(engine.Evaluate("foo.Property('jkl')"));

            engine.Execute("foo.Property.Item.set(DayOfWeek.Saturday, -123)");
            Assert.AreEqual(-123, engine.Evaluate("foo.Property(DayOfWeek.Saturday)"));
            Assert.AreEqual(-123, engine.Evaluate("foo.Property.Item(DayOfWeek.Saturday)"));
            Assert.AreEqual(-123, engine.Evaluate("foo.Property.Item.get(DayOfWeek.Saturday)"));
            Assert.IsNull(engine.Evaluate("foo.Property(DayOfWeek.Sunday)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DefaultProperty_MethodTunneling()
        {
            engine.Script.foo = new DefaultPropertyTestContainer();
            engine.AddHostType("DayOfWeek", typeof(DayOfWeek));

            engine.Execute("foo.Method().Item.set('ghi', 321)");
            Assert.AreEqual(321, engine.Evaluate("foo.Method()('ghi')"));
            Assert.AreEqual(321, engine.Evaluate("foo.Method().Item('ghi')"));
            Assert.AreEqual(321, engine.Evaluate("foo.Method().Item.get('ghi')"));
            Assert.IsNull(engine.Evaluate("foo.Method()('jkl')"));

            engine.Execute("foo.Method().Item.set(DayOfWeek.Saturday, -123)");
            Assert.AreEqual(-123, engine.Evaluate("foo.Method()(DayOfWeek.Saturday)"));
            Assert.AreEqual(-123, engine.Evaluate("foo.Method().Item(DayOfWeek.Saturday)"));
            Assert.AreEqual(-123, engine.Evaluate("foo.Method().Item.get(DayOfWeek.Saturday)"));
            Assert.IsNull(engine.Evaluate("foo.Method()(DayOfWeek.Sunday)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DefaultProperty_Indexer()
        {
            engine.Script.dict = new Dictionary<string, object> { { "abc", 123 }, { "def", 456 }, { "ghi", 789 } };
            engine.Execute("item = dict.Item");

            Assert.AreEqual(123, engine.Evaluate("item('abc')"));
            Assert.AreEqual(456, engine.Evaluate("item('def')"));
            Assert.AreEqual(789, engine.Evaluate("item('ghi')"));
            TestUtil.AssertException<KeyNotFoundException>(() => engine.Evaluate("item('jkl')"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_PropertyAndMethodWithSameName()
        {
            engine.AddHostObject("lib", HostItemFlags.GlobalMembers, new HostTypeCollection("mscorlib", "System", "System.Core"));

            engine.Script.dict = new Dictionary<string, object> { { "abc", 123 }, { "def", 456 }, { "ghi", 789 } };
            Assert.AreEqual(3, engine.Evaluate("dict.Count"));
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("dict.Count()"));

            engine.Script.listDict = new ListDictionary { { "abc", 123 }, { "def", 456 }, { "ghi", 789 } };
            Assert.AreEqual(3, engine.Evaluate("listDict.Count"));
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("listDict.Count()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_toFunction_Delegate()
        {
            engine.Script.foo = new Func<int, double>(arg => arg * Math.PI);
            Assert.AreEqual(123 * Math.PI, engine.Evaluate("foo(123)"));
            Assert.AreEqual("function", engine.Evaluate("typeof foo.toFunction"));
            Assert.AreEqual("function", engine.Evaluate("typeof foo.toFunction()"));
            Assert.AreEqual(456 * Math.PI, engine.Evaluate("foo.toFunction()(456)"));
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("new foo()"));
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("new (foo.toFunction())()"));

            engine.Script.bar = new VarArgDelegate((pre, args) => args.Aggregate((int)pre, (value, arg) => value + (int)arg));
            Assert.AreEqual(3330, engine.Evaluate("bar(123, 456, 789, 987, 654, 321)"));
            Assert.AreEqual("function", engine.Evaluate("typeof bar.toFunction"));
            Assert.AreEqual("function", engine.Evaluate("typeof bar.toFunction()"));
            Assert.AreEqual(2934, engine.Evaluate("bar.toFunction()(135, 579, 975, 531, 135, 579)"));
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("new bar()"));
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("new (bar.toFunction())()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_toFunction_Method()
        {
            engine.Script.host = new HostFunctions();
            Assert.AreEqual("function", engine.Evaluate("typeof host.newObj.toFunction"));
            Assert.AreEqual("function", engine.Evaluate("typeof host.newObj.toFunction()"));
            Assert.IsInstanceOfType(engine.Evaluate("host.newObj()"), typeof(PropertyBag));
            Assert.IsInstanceOfType(engine.Evaluate("host.newObj.toFunction()()"), typeof(PropertyBag));
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("new host.newObj()"));
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("new (host.newObj.toFunction())()"));

            engine.AddHostType(typeof(Random));
            Assert.IsInstanceOfType(engine.Evaluate("host.newObj(Random, 100)"), typeof(Random));
            Assert.IsInstanceOfType(engine.Evaluate("host.newObj.toFunction()(Random, 100)"), typeof(Random));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_toFunction_Type()
        {
            engine.AddHostType(typeof(Random));
            Assert.AreEqual("function", engine.Evaluate("typeof Random.toFunction"));
            Assert.AreEqual("function", engine.Evaluate("typeof Random.toFunction()"));
            Assert.IsInstanceOfType(engine.Evaluate("new Random()"), typeof(Random));
            Assert.IsInstanceOfType(engine.Evaluate("new Random(100)"), typeof(Random));
            Assert.IsInstanceOfType(engine.Evaluate("new (Random.toFunction())()"), typeof(Random));
            Assert.IsInstanceOfType(engine.Evaluate("new (Random.toFunction())(100)"), typeof(Random));
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("Random(100)"));
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("(Random.toFunction())(100)"));

            engine.AddHostType(typeof(Dictionary<,>));
            engine.AddHostType(typeof(int));
            Assert.AreEqual("function", engine.Evaluate("typeof Dictionary.toFunction"));
            Assert.AreEqual("function", engine.Evaluate("typeof Dictionary.toFunction()"));
            Assert.IsInstanceOfType(engine.Evaluate("Dictionary(Int32, Int32)"), typeof(HostType));
            Assert.IsInstanceOfType(engine.Evaluate("Dictionary.toFunction()(Int32, Int32)"), typeof(HostType));
            Assert.IsInstanceOfType(engine.Evaluate("new Dictionary(Int32, Int32, 100)"), typeof(Dictionary<int, int>));
            Assert.IsInstanceOfType(engine.Evaluate("new (Dictionary.toFunction())(Int32, Int32, 100)"), typeof(Dictionary<int, int>));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_toFunction_None()
        {
            engine.Script.foo = new Random();
            Assert.IsInstanceOfType(engine.Evaluate("foo"), typeof(Random));
            Assert.IsInstanceOfType(engine.Evaluate("foo.toFunction"), typeof(Undefined));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration()
        {
            var array = Enumerable.Range(0, 10).ToArray();
            engine.Execute(@"
                function sum(array) {
                    var result = 0;
                    for (var item of array) {
                        result += item;
                    }
                    return result;
                }
            ");

            // run test several times to verify workaround for V8 optimizer bug
            for (var i = 0; i < 64; i++)
            {
                Assert.AreEqual(array.Aggregate((current, next) => current + next), engine.Script.sum(array));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                var array = Enumerable.Range(0, 10).ToArray();
                engine.Execute(@"
                    function sum(array) {
                        var result = 0;
                        for (var item of array) {
                            result += item;
                        }
                        return result;
                    }
                ");

                // run test several times to verify workaround for V8 optimizer bug
                for (var i = 0; i < 64; i++)
                {
                    Assert.AreEqual(array.Aggregate((current, next) => current + next), engine.Script.sum(array));
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            var array = Enumerable.Range(0, 10).ToArray();
            engine.Execute(@"
                function sum(array) {
                    var result = 0;
                    for (var item of array) {
                        result += item;
                    }
                    return result;
                }
            ");

            // run test several times to verify workaround for V8 optimizer bug
            for (var i = 0; i < 64; i++)
            {
                Assert.AreEqual(array.Aggregate((current, next) => current + next), engine.Script.sum(array));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_Generic()
        {
            var array = Enumerable.Range(0, 10).Select(value => (IConvertible)value).ToArray();
            engine.Script.culture = CultureInfo.InvariantCulture;
            engine.Execute(@"
                function sum(array) {
                    var result = 0;
                    for (var item of array) {
                        result += item.ToInt32(culture);
                    }
                    return result;
                }
            ");

            // run test several times to verify workaround for V8 optimizer bug
            for (var i = 0; i < 64; i++)
            {
                Assert.AreEqual(array.Aggregate((current, next) => Convert.ToInt32(current) + Convert.ToInt32(next)), engine.Script.sum(array));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_Generic_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                var array = Enumerable.Range(0, 10).Select(value => (IConvertible)value).ToArray();
                engine.Script.culture = CultureInfo.InvariantCulture;
                engine.Execute(@"
                    function sum(array) {
                        var result = 0;
                        for (var item of array) {
                            result += item.toInt32(culture);
                        }
                        return result;
                    }
                ");

                // run test several times to verify workaround for V8 optimizer bug
                for (var i = 0; i < 64; i++)
                {
                    Assert.AreEqual(array.Aggregate((current, next) => Convert.ToInt32(current) + Convert.ToInt32(next)), engine.Script.sum(array));
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_Generic_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            var array = Enumerable.Range(0, 10).Select(value => (IConvertible)value).ToArray();
            engine.Execute(@"
                function sum(array) {
                    var result = 0;
                    for (var item of array) {
                        result += item;
                    }
                    return result;
                }
            ");

            // run test several times to verify workaround for V8 optimizer bug
            for (var i = 0; i < 64; i++)
            {
                Assert.AreEqual(array.Aggregate((current, next) => Convert.ToInt32(current) + Convert.ToInt32(next)), engine.Script.sum(array));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_NonGeneric()
        {
            var array = Enumerable.Range(0, 10).ToArray();
            engine.Execute(@"
                function sum(array) {
                    var result = 0;
                    for (var item of array) {
                        result += item;
                    }
                    return result;
                }
            ");

            // run test several times to verify workaround for V8 optimizer bug
            for (var i = 0; i < 64; i++)
            {
                Assert.AreEqual(array.Aggregate((current, next) => current + next), engine.Script.sum(HostObject.Wrap(array, typeof(IEnumerable))));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_NonGeneric_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                var array = Enumerable.Range(0, 10).ToArray();
                engine.Execute(@"
                    function sum(array) {
                        var result = 0;
                        for (var item of array) {
                            result += item;
                        }
                        return result;
                    }
                ");

                // run test several times to verify workaround for V8 optimizer bug
                for (var i = 0; i < 64; i++)
                {
                    Assert.AreEqual(array.Aggregate((current, next) => current + next), engine.Script.sum(HostObject.Wrap(array, typeof(IEnumerable))));
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_NonGeneric_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            var array = Enumerable.Range(0, 10).ToArray();
            engine.Execute(@"
                function sum(array) {
                    var result = 0;
                    for (var item of array) {
                        result += item;
                    }
                    return result;
                }
            ");

            // run test several times to verify workaround for V8 optimizer bug
            for (var i = 0; i < 64; i++)
            {
                Assert.AreEqual(array.Aggregate((current, next) => current + next), engine.Script.sum(HostObject.Wrap(array, typeof(IEnumerable))));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_NonEnumerable()
        {
            engine.Execute(@"
                function sum(array) {
                    var result = 0;
                    for (var item of array) {
                        result += item;
                    }
                    return result;
                }
            ");

            // run test several times to verify workaround for V8 optimizer bug
            for (var i = 0; i < 64; i++)
            {
                TestUtil.AssertException<NotSupportedException>(() => engine.Script.sum(DayOfWeek.Monday));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_NonEnumerable_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                engine.Execute(@"
                    function sum(array) {
                        var result = 0;
                        for (var item of array) {
                            result += item;
                        }
                        return result;
                    }
                ");

                // run test several times to verify workaround for V8 optimizer bug
                for (var i = 0; i < 64; i++)
                {
                    TestUtil.AssertException<NotSupportedException>(() => engine.Script.sum(DayOfWeek.Monday));
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_NonEnumerable_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            engine.Execute(@"
                function sum(array) {
                    var result = 0;
                    for (var item of array) {
                        result += item;
                    }
                    return result;
                }
            ");

            // run test several times to verify workaround for V8 optimizer bug
            for (var i = 0; i < 64; i++)
            {
                TestUtil.AssertException<NotSupportedException>(() => engine.Script.sum(DayOfWeek.Monday));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_Disposal()
        {
            var source = TestEnumerable.Create("foo", "bar", "baz");

            engine.AddRestrictedHostObject("source", source);
            engine.Execute(@"
                result = '';
                for (let item of source) {
                    result += item;
                }
            ");

            Assert.AreEqual("foobarbaz", engine.Script.result);
            Assert.AreEqual(1, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);

            engine.Script.done = new ManualResetEventSlim();
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (let item of source) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            Assert.AreEqual("foobarbaz", engine.Script.result);
            Assert.AreEqual(2, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_Disposal_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                var source = TestEnumerable.Create("foo", "bar", "baz");

                engine.AddRestrictedHostObject("source", source);
                engine.Execute(@"
                    result = '';
                    for (let item of source) {
                        result += item;
                    }
                ");

                Assert.AreEqual("foobarbaz", engine.Script.result);
                Assert.AreEqual(1, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);

                engine.Script.done = new ManualResetEventSlim();
                engine.Execute(@"
                    result = '';
                    (async function () {
                        for await (let item of source) {
                            result += item;
                        }
                        done.set();
                    })();
                ");
                engine.Script.done.Wait();

                Assert.AreEqual("foobarbaz", engine.Script.result);
                Assert.AreEqual(2, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_Disposal_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            var source = TestEnumerable.Create("foo", "bar", "baz");

            engine.AddRestrictedHostObject("source", source);
            engine.Execute(@"
                result = '';
                for (let item of source) {
                    result += item;
                }
            ");

            Assert.AreEqual("foobarbaz", engine.Script.result);
            Assert.AreEqual(1, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);

            engine.Script.done = new ManualResetEventSlim();
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (let item of source) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            Assert.AreEqual("foobarbaz", engine.Script.result);
            Assert.AreEqual(2, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_Disposal_GenericSource()
        {
            var source = TestEnumerable.CreateGeneric("foo", "bar", "baz");

            engine.AddRestrictedHostObject("source", source);
            engine.Execute(@"
                result = '';
                for (let item of source) {
                    result += item;
                }
            ");

            Assert.AreEqual("foobarbaz", engine.Script.result);
            Assert.AreEqual(1, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);

            engine.Script.done = new ManualResetEventSlim();
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (let item of source) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            Assert.AreEqual("foobarbaz", engine.Script.result);
            Assert.AreEqual(2, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_Disposal_GenericSource_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                var source = TestEnumerable.CreateGeneric("foo", "bar", "baz");

                engine.AddRestrictedHostObject("source", source);
                engine.Execute(@"
                    result = '';
                    for (let item of source) {
                        result += item;
                    }
                ");

                Assert.AreEqual("foobarbaz", engine.Script.result);
                Assert.AreEqual(1, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);

                engine.Script.done = new ManualResetEventSlim();
                engine.Execute(@"
                    result = '';
                    (async function () {
                        for await (let item of source) {
                            result += item;
                        }
                        done.set();
                    })();
                ");
                engine.Script.done.Wait();

                Assert.AreEqual("foobarbaz", engine.Script.result);
                Assert.AreEqual(2, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_Disposal_GenericSource_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            var source = TestEnumerable.CreateGeneric("foo", "bar", "baz");

            engine.AddRestrictedHostObject("source", source);
            engine.Execute(@"
                result = '';
                for (let item of source) {
                    result += item;
                }
            ");

            Assert.AreEqual("foobarbaz", engine.Script.result);
            Assert.AreEqual(1, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);

            engine.Script.done = new ManualResetEventSlim();
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (let item of source) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            Assert.AreEqual("foobarbaz", engine.Script.result);
            Assert.AreEqual(2, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_Disposal_AsyncSource()
        {
            var source = TestEnumerable.CreateAsync("foo", "bar", "baz");

            engine.Script.done = new ManualResetEventSlim();
            engine.AddRestrictedHostObject("source", source);
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (let item of source) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            Assert.AreEqual("foobarbaz", engine.Script.result);
            Assert.AreEqual(1, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_Disposal_AsyncSource_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                var source = TestEnumerable.CreateAsync("foo", "bar", "baz");

                engine.Script.done = new ManualResetEventSlim();
                engine.AddRestrictedHostObject("source", source);
                engine.Execute(@"
                    result = '';
                    (async function () {
                        for await (let item of source) {
                            result += item;
                        }
                        done.set();
                    })();
                ");
                engine.Script.done.Wait();

                Assert.AreEqual("foobarbaz", engine.Script.result);
                Assert.AreEqual(1, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Iteration_Disposal_AsyncSource_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            var source = TestEnumerable.CreateAsync("foo", "bar", "baz");

            engine.Script.done = new ManualResetEventSlim();
            engine.AddRestrictedHostObject("source", source);
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (let item of source) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            Assert.AreEqual("foobarbaz", engine.Script.result);
            Assert.AreEqual(1, ((TestEnumerable.IDisposableEnumeratorFactory)source).DisposedEnumeratorCount);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_PropertyBag()
        {
            engine.Script.done = new ManualResetEventSlim();
            engine.Script.enumerable = new PropertyBag { ["foo"] = 123, ["bar"] = "blah" };
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (var item of enumerable) {
                        result += item.Value;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            var result = (string)engine.Script.result;
            Assert.AreEqual(7, result.Length);
            Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_PropertyBag_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                engine.Script.done = new ManualResetEventSlim();
                engine.Script.enumerable = new PropertyBag { ["foo"] = 123, ["bar"] = "blah" };
                engine.Execute(@"
                    result = '';
                    (async function () {
                        for await (var item of enumerable) {
                            result += item.value;
                        }
                        done.set();
                    })();
                ");
                engine.Script.done.Wait();

                var result = (string)engine.Script.result;
                Assert.AreEqual(7, result.Length);
                Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_PropertyBag_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            engine.Script.done = new ManualResetEventSlim();
            engine.Script.enumerable = new PropertyBag { ["foo"] = 123, ["bar"] = "blah" };
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (var item of enumerable) {
                        result += item.Value;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            var result = (string)engine.Script.result;
            Assert.AreEqual(7, result.Length);
            Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_List()
        {
            engine.Script.done = new ManualResetEventSlim();
            engine.Script.enumerable = new List<object> { 123, "blah" };
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (var item of enumerable) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            var result = (string)engine.Script.result;
            Assert.AreEqual(7, result.Length);
            Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_List_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                engine.Script.done = new ManualResetEventSlim();
                engine.Script.enumerable = new List<object> { 123, "blah" };
                engine.Execute(@"
                    result = '';
                    (async function () {
                        for await (var item of enumerable) {
                            result += item;
                        }
                        done.set();
                    })();
                ");
                engine.Script.done.Wait();

                var result = (string)engine.Script.result;
                Assert.AreEqual(7, result.Length);
                Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_List_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            engine.Script.done = new ManualResetEventSlim();
            engine.Script.enumerable = new List<object> { 123, "blah" };
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (var item of enumerable) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            var result = (string)engine.Script.result;
            Assert.AreEqual(7, result.Length);
            Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_ArrayList()
        {
            engine.Script.done = new ManualResetEventSlim();
            engine.Script.enumerable = new ArrayList { 123, "blah" };
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (var item of enumerable) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            var result = (string)engine.Script.result;
            Assert.AreEqual(7, result.Length);
            Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_ArrayList_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                engine.Script.done = new ManualResetEventSlim();
                engine.Script.enumerable = new ArrayList { 123, "blah" };
                engine.Execute(@"
                    result = '';
                    (async function () {
                        for await (var item of enumerable) {
                            result += item;
                        }
                        done.set();
                    })();
                ");
                engine.Script.done.Wait();

                var result = (string)engine.Script.result;
                Assert.AreEqual(7, result.Length);
                Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_ArrayList_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            engine.Script.done = new ManualResetEventSlim();
            engine.Script.enumerable = new ArrayList { 123, "blah" };
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (var item of enumerable) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            var result = (string)engine.Script.result;
            Assert.AreEqual(7, result.Length);
            Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_Array()
        {
            engine.Script.done = new ManualResetEventSlim();
            engine.Script.enumerable = new object[] { 123, "blah" };
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (var item of enumerable) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            var result = (string)engine.Script.result;
            Assert.AreEqual(7, result.Length);
            Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_Array_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                engine.Script.done = new ManualResetEventSlim();
                engine.Script.enumerable = new object[] { 123, "blah" };
                engine.Execute(@"
                    result = '';
                    (async function () {
                        for await (var item of enumerable) {
                            result += item;
                        }
                        done.set();
                    })();
                ");
                engine.Script.done.Wait();

                var result = (string)engine.Script.result;
                Assert.AreEqual(7, result.Length);
                Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_Array_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            engine.Script.done = new ManualResetEventSlim();
            engine.Script.enumerable = new object[] { 123, "blah" };
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (var item of enumerable) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            var result = (string)engine.Script.result;
            Assert.AreEqual(7, result.Length);
            Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_AsyncEnumerable()
        {
            static async IAsyncEnumerable<object> GetItems()
            {
                await Task.Delay(10);
                yield return 123;
                await Task.Delay(10);
                yield return "blah";
            }

            engine.Script.done = new ManualResetEventSlim();
            engine.Script.enumerable = GetItems();
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (var item of enumerable) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            var result = (string)engine.Script.result;
            Assert.AreEqual(7, result.Length);
            Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_AsyncEnumerable_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                static async IAsyncEnumerable<object> GetItems()
                {
                    await Task.Delay(10);
                    yield return 123;
                    await Task.Delay(10);
                    yield return "blah";
                }

                engine.Script.done = new ManualResetEventSlim();
                engine.Script.enumerable = GetItems();
                engine.Execute(@"
                    result = '';
                    (async function () {
                        for await (var item of enumerable) {
                            result += item;
                        }
                        done.set();
                    })();
                ");
                engine.Script.done.Wait();

                var result = (string)engine.Script.result;
                Assert.AreEqual(7, result.Length);
                Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_AsyncEnumerable_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            static async IAsyncEnumerable<object> GetItems()
            {
                await Task.Delay(10);
                yield return 123;
                await Task.Delay(10);
                yield return "blah";
            }

            engine.Script.done = new ManualResetEventSlim();
            engine.Script.enumerable = GetItems();
            engine.Execute(@"
                result = '';
                (async function () {
                    for await (var item of enumerable) {
                        result += item;
                    }
                    done.Set();
                })();
            ");
            engine.Script.done.Wait();

            var result = (string)engine.Script.result;
            Assert.AreEqual(7, result.Length);
            Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_AsyncEnumerable_Exception()
        {
            const string errorMessage = "Well, this is bogus!";
            static async IAsyncEnumerable<object> GetItems()
            {
                await Task.Delay(10);
                yield return 123;
                await Task.Delay(10);
                yield return "blah";
                throw new InvalidOperationException(errorMessage);
            }

            engine.Script.done = new ManualResetEventSlim();
            engine.Script.enumerable = GetItems();
            engine.Execute(@"
                result = '';
                (async function () {
                    try {
                        for await (var item of enumerable) {
                            result += item;
                        }
                    }
                    catch (error) {
                        errorMessage = error.message;
                        throw error;
                    }
                    finally {
                        done.Set();
                    }
                })();
            ");
            engine.Script.done.Wait();

            var result = (string)engine.Script.result;
            Assert.AreEqual(7, result.Length);
            Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
            Assert.AreEqual(errorMessage, engine.Script.errorMessage);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_AsyncEnumerable_Exception_GlobalRenaming()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();

                const string errorMessage = "Well, this is bogus!";
                static async IAsyncEnumerable<object> GetItems()
                {
                    await Task.Delay(10);
                    yield return 123;
                    await Task.Delay(10);
                    yield return "blah";
                    throw new InvalidOperationException(errorMessage);
                }

                engine.Script.done = new ManualResetEventSlim();
                engine.Script.enumerable = GetItems();
                engine.Execute(@"
                    result = '';
                    (async function () {
                        try {
                            for await (var item of enumerable) {
                                result += item;
                            }
                        }
                        catch (error) {
                            errorMessage = error.message;
                            throw error;
                        }
                        finally {
                            done.set();
                        }
                    })();
                ");
                engine.Script.done.Wait();

                var result = (string)engine.Script.result;
                Assert.AreEqual(7, result.Length);
                Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
                Assert.AreEqual(errorMessage, engine.Script.errorMessage);
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_AsyncIteration_AsyncEnumerable_Exception_DisableTypeRestriction()
        {
            engine.DisableTypeRestriction = true;

            const string errorMessage = "Well, this is bogus!";

            static async IAsyncEnumerable<object> GetItems()
            {
                await Task.Delay(10);
                yield return 123;
                await Task.Delay(10);
                yield return "blah";
                throw new InvalidOperationException(errorMessage);
            }

            engine.Script.done = new ManualResetEventSlim();
            engine.Script.enumerable = GetItems();
            engine.Execute(@"
                result = '';
                (async function () {
                    try {
                        for await (var item of enumerable) {
                            result += item;
                        }
                    }
                    catch (error) {
                        errorMessage = error.message;
                        throw error;
                    }
                    finally {
                        done.Set();
                    }
                })();
            ");
            engine.Script.done.Wait();

            var result = (string)engine.Script.result;
            Assert.AreEqual(7, result.Length);
            Assert.IsTrue(result.IndexOf("123", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(result.IndexOf("blah", StringComparison.Ordinal) >= 0);
            Assert.AreEqual(errorMessage, engine.Script.errorMessage);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_SuppressInstanceMethodEnumeration()
        {
            engine.Script.foo = Enumerable.Range(0, 25).ToArray();
            Assert.AreEqual("ToString", engine.Evaluate("Object.keys(foo).find(function (key) { return key == 'ToString' })"));
            Assert.IsInstanceOfType(engine.Evaluate("foo.ToString"), typeof(HostMethod));
            Assert.AreEqual("System.Int32[]", engine.Evaluate("foo.ToString()"));

            engine.SuppressInstanceMethodEnumeration = true;
            Assert.IsInstanceOfType(engine.Evaluate("Object.keys(foo).find(function (key) { return key == 'ToString' })"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("foo.ToString"), typeof(HostMethod));
            Assert.AreEqual("System.Int32[]", engine.Evaluate("foo.ToString()"));

            engine.SuppressInstanceMethodEnumeration = false;
            Assert.AreEqual("ToString", engine.Evaluate("Object.keys(foo).find(function (key) { return key == 'ToString' })"));
            Assert.IsInstanceOfType(engine.Evaluate("foo.ToString"), typeof(HostMethod));
            Assert.AreEqual("System.Int32[]", engine.Evaluate("foo.ToString()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_SuppressExtensionMethodEnumeration()
        {
            engine.AddHostType(typeof(Enumerable));
            engine.Script.foo = Enumerable.Range(0, 25).ToArray();
            Assert.AreEqual("Count", engine.Evaluate("Object.keys(foo).find(function (key) { return key == 'Count' })"));
            Assert.IsInstanceOfType(engine.Evaluate("foo.Count"), typeof(HostMethod));
            Assert.AreEqual(25, engine.Evaluate("foo.Count()"));

            engine.SuppressExtensionMethodEnumeration = true;
            Assert.IsInstanceOfType(engine.Evaluate("Object.keys(foo).find(function (key) { return key == 'Count' })"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("foo.Count"), typeof(HostMethod));
            Assert.AreEqual(25, engine.Evaluate("foo.Count()"));

            engine.SuppressExtensionMethodEnumeration = false;
            Assert.AreEqual("Count", engine.Evaluate("Object.keys(foo).find(function (key) { return key == 'Count' })"));
            Assert.IsInstanceOfType(engine.Evaluate("foo.Count"), typeof(HostMethod));
            Assert.AreEqual(25, engine.Evaluate("foo.Count()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ScriptObject()
        {
            var obj = engine.Evaluate("({})") as ScriptObject;
            Assert.IsNotNull(obj);
            Assert.AreSame(engine, obj.Engine);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DateTimeConversion()
        {
            engine.Script.now = DateTime.Now;
            Assert.AreEqual("HostObject", engine.Evaluate("now.constructor.name"));

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableDateTimeConversion);
            var utcEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

            var now = DateTime.Now;
            engine.Script.now = now;
            Assert.AreEqual("Date", engine.Evaluate("now.constructor.name"));
            Assert.IsTrue(Math.Abs((now.ToUniversalTime() - utcEpoch).TotalMilliseconds - Convert.ToDouble(engine.Evaluate("now.valueOf()"))) <= 1.0);

            var utcNow = DateTime.UtcNow;
            engine.Script.now = utcNow;
            Assert.AreEqual("Date", engine.Evaluate("now.constructor.name"));
            Assert.IsTrue(Math.Abs((utcNow - utcEpoch).TotalMilliseconds - Convert.ToDouble(engine.Evaluate("now.valueOf()"))) <= 1.0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_TaskPromiseConversion()
        {
            engine.Script.value = Task.FromResult("foo");
            Assert.AreEqual("HostObject", engine.Evaluate("value.constructor.name"));
            Assert.IsInstanceOfType(engine.Evaluate("Promise.resolve(123)"), typeof(ScriptObject));

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableTaskPromiseConversion);

            engine.Script.value = Task.FromResult("bar");
            Assert.AreEqual("Promise", engine.Evaluate("value.constructor.name"));
            Assert.IsInstanceOfType(engine.Evaluate("Promise.resolve(123)"), typeof(Task));

            var task = new Func<Task<object>>(async () => await (Task<object>)engine.Evaluate("Promise.resolve(123)"))();
            Assert.AreEqual(123, task.Result);

            engine.Script.promise = Task.FromResult(456);
            engine.Execute("promise.then(value => result = value);");
            Assert.AreEqual(456, engine.Script.result);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public async Task V8ScriptEngine_TaskPromiseConversion_TaskOptimizations()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableTaskPromiseConversion);
            engine.AddHostType(typeof(Task));

            var task = (Task<object>)engine.Evaluate("(async function () { await Task.Delay(500); return 'foo'; })()");
            Assert.IsFalse(task.IsCompleted);
            Assert.AreEqual("foo", await task);

            task = (Task<object>)engine.Evaluate("(async function () { await Task.Delay(500); throw new Error('Huh?'); })()");
            Assert.IsFalse(task.IsCompleted);

            var gotException = false;
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                Assert.IsTrue(exception.Message.Contains("Huh?"));
                gotException = true;
            }
            Assert.IsTrue(gotException);

            task = (Task<object>)engine.Evaluate("(async function () { return 'bar'; })()");
            Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
            Assert.AreEqual("bar", await task);

            task = (Task<object>)engine.Evaluate("(async function () { throw new Error('Blah!'); })()");
            Assert.IsTrue(task.IsFaulted);

            gotException = false;
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                Assert.IsTrue(exception.Message.Contains("Blah!"));
                gotException = true;
            }
            Assert.IsTrue(gotException);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public async Task V8ScriptEngine_TaskPromiseConversion_PromiseOptimizations()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableTaskPromiseConversion);

            engine.Script.promise = Task.Delay(500);
            Assert.AreEqual(0, engine.Evaluate("EngineInternal.getPromiseState(promise)"));
            Assert.IsInstanceOfType(engine.Evaluate("EngineInternal.getPromiseResult(promise)"), typeof(Undefined));
            Assert.AreEqual(123, await (Task<object>)engine.Evaluate("(async function () { await promise; return 123; })()"));

            engine.Script.promise = Task.Run(async () => { await Task.Delay(500); throw new InvalidOperationException("Boom!"); });
            Assert.AreEqual(0, engine.Evaluate("EngineInternal.getPromiseState(promise)"));
            Assert.IsInstanceOfType(engine.Evaluate("EngineInternal.getPromiseResult(promise)"), typeof(Undefined));

            var gotException = false;
            try
            {
                await (Task<object>)engine.Evaluate("(async function () { await promise; })()");
            }
            catch (Exception exception)
            {
                Assert.IsTrue(exception.Message.Contains("Boom!"));
                gotException = true;
            }
            Assert.IsTrue(gotException);

            engine.Script.promise = Task.CompletedTask;
            Assert.AreEqual(1, engine.Evaluate("EngineInternal.getPromiseState(promise)"));
            Assert.IsInstanceOfType(engine.Evaluate("EngineInternal.getPromiseResult(promise)"), typeof(Undefined));
            Assert.AreEqual(456, await (Task<object>)engine.Evaluate("(async function () { await promise; return 456; })()"));

            engine.Script.promise = Task.FromException(new InvalidOperationException("Meh?!"));
            Assert.AreEqual(2, engine.Evaluate("EngineInternal.getPromiseState(promise)"));
            Assert.IsTrue((engine.Evaluate("EngineInternal.getPromiseResult(promise)") is ScriptObject error1) && ((string)error1["message"]).Contains("Meh?!"));

            gotException = false;
            try
            {
                await (Task<object>)engine.Evaluate("(async function () { await promise; })()");
            }
            catch (Exception exception)
            {
                Assert.IsTrue(exception.Message.Contains("Meh?!"));
                gotException = true;
            }
            Assert.IsTrue(gotException);

            engine.Script.promise = Task.Run(async () => { await Task.Delay(500); return 789; });
            Assert.AreEqual(0, engine.Evaluate("EngineInternal.getPromiseState(promise)"));
            Assert.IsInstanceOfType(engine.Evaluate("EngineInternal.getPromiseResult(promise)"), typeof(Undefined));
            Assert.AreEqual(789, await (Task<object>)engine.Evaluate("(async function () { return await promise; })()"));

            engine.Script.promise = Task.FromResult(987);
            Assert.AreEqual(1, engine.Evaluate("EngineInternal.getPromiseState(promise)"));
            Assert.AreEqual(987, engine.Evaluate("EngineInternal.getPromiseResult(promise)"));
            Assert.AreEqual(987, await (Task<object>)engine.Evaluate("(async function () { return await promise; })()"));

            engine.Script.promise = Task.FromException<int>(new InvalidOperationException("Yuck!?"));
            Assert.AreEqual(2, engine.Evaluate("EngineInternal.getPromiseState(promise)"));
            Assert.IsTrue((engine.Evaluate("EngineInternal.getPromiseResult(promise)") is ScriptObject error2) && ((string)error2["message"]).Contains("Yuck!?"));

            gotException = false;
            try
            {
                await (Task<object>)engine.Evaluate("(async function () { await promise; })()");
            }
            catch (Exception exception)
            {
                Assert.IsTrue(exception.Message.Contains("Yuck!?"));
                gotException = true;
            }
            Assert.IsTrue(gotException);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ValueTaskPromiseConversion()
        {
            engine.Script.value = new ValueTask<string>("foo");
            Assert.AreEqual("HostObject", engine.Evaluate("value.constructor.name"));
            Assert.IsInstanceOfType(engine.Evaluate("Promise.resolve(123)"), typeof(ScriptObject));

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableTaskPromiseConversion | V8ScriptEngineFlags.EnableValueTaskPromiseConversion);

            engine.Script.value = new ValueTask<string>("bar");
            Assert.AreEqual("Promise", engine.Evaluate("value.constructor.name"));
            Assert.IsInstanceOfType(engine.Evaluate("Promise.resolve(123)"), typeof(Task));

            var task = new Func<Task<object>>(async () => await (Task<object>)engine.Evaluate("Promise.resolve(123)"))();
            Assert.AreEqual(123, task.Result);

            engine.Script.promise = new ValueTask<int>(456);
            engine.Execute("promise.then(value => result = value);");
            Assert.AreEqual(456, engine.Script.result);

            var cancelSource = new CancellationTokenSource();
            cancelSource.Cancel();
            engine.Script.promise = new ValueTask<string>(Task<string>.Factory.StartNew(() => "baz", cancelSource.Token));
            Thread.Sleep(250);
            engine.Execute("promise.then(value => result = value, value => error = value);");
            Assert.IsInstanceOfType(engine.Script.error.hostException.GetBaseException(), typeof(TaskCanceledException));

            cancelSource = new CancellationTokenSource();
            engine.Script.promise = new ValueTask<double>(Task<double>.Factory.StartNew(() => throw new ConstraintException(), cancelSource.Token));
            Thread.Sleep(250);
            engine.Execute("promise.then(value => result = value, value => error = value);");
            Assert.IsInstanceOfType(engine.Script.error.hostException.GetBaseException(), typeof(ConstraintException));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ValueTaskPromiseConversion_NoResult()
        {
            engine.Script.value = new ValueTask(Task.CompletedTask);
            Assert.AreEqual("HostObject", engine.Evaluate("value.constructor.name"));
            Assert.IsInstanceOfType(engine.Evaluate("Promise.resolve(123)"), typeof(ScriptObject));

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableTaskPromiseConversion | V8ScriptEngineFlags.EnableValueTaskPromiseConversion);

            engine.Script.value = new ValueTask(Task.CompletedTask);
            Assert.AreEqual("Promise", engine.Evaluate("value.constructor.name"));
            Assert.IsInstanceOfType(engine.Evaluate("Promise.resolve(123)"), typeof(Task));

            var task = new Func<Task<object>>(async () => await (Task<object>)engine.Evaluate("Promise.resolve(123)"))();
            Assert.AreEqual(123, task.Result);

            engine.Script.promise = new ValueTask(Task.CompletedTask);
            engine.Execute("promise.then(value => result = value);");
            Assert.IsInstanceOfType(engine.Script.result, typeof(Undefined));

            var cancelSource = new CancellationTokenSource();
            cancelSource.Cancel();
            engine.Script.promise = new ValueTask(Task.Factory.StartNew(() => {}, cancelSource.Token));
            Thread.Sleep(250);
            engine.Execute("promise.then(value => result = value, value => error = value);");
            Assert.IsInstanceOfType(engine.Script.error.hostException.GetBaseException(), typeof(TaskCanceledException));

            cancelSource = new CancellationTokenSource();
            engine.Script.promise = new ValueTask(Task.Factory.StartNew(() => throw new ConstraintException(), cancelSource.Token));
            Thread.Sleep(250);
            engine.Execute("promise.then(value => result = value, value => error = value);");
            Assert.IsInstanceOfType(engine.Script.error.hostException.GetBaseException(), typeof(ConstraintException));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DateTimeConversion_FromScript()
        {
            Assert.IsInstanceOfType(engine.Evaluate("new Date(Date.now())"), typeof(ScriptObject));

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableDateTimeConversion);
            var utcEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

            var utcNowObj = engine.Evaluate("now = new Date(Date.now())");
            Assert.IsInstanceOfType(utcNowObj, typeof(DateTime));
            Assert.AreEqual(DateTimeKind.Utc, ((DateTime)utcNowObj).Kind);
            Assert.IsTrue(Math.Abs(((DateTime)utcNowObj - utcEpoch).TotalMilliseconds - Convert.ToDouble(engine.Evaluate("now.valueOf()"))) <= 1.0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_typeof()
        {
            engine.Script.foo = new Random();
            Assert.AreEqual("object", engine.Evaluate("typeof foo"));
            Assert.IsTrue((bool)engine.Evaluate("typeof foo === 'object'"));
            Assert.AreEqual("function", engine.Evaluate("typeof foo.ToString"));
            Assert.IsTrue((bool)engine.Evaluate("typeof foo.ToString === 'function'"));

            engine.Script.foo = Enumerable.Range(0, 5).ToArray();
            Assert.AreEqual("object", engine.Evaluate("typeof foo"));
            Assert.IsTrue((bool)engine.Evaluate("typeof foo === 'object'"));

            engine.Script.foo = new ArrayList();
            Assert.AreEqual("object", engine.Evaluate("typeof foo"));
            Assert.IsTrue((bool)engine.Evaluate("typeof foo === 'object'"));

            engine.Script.foo = new BitArray(100);
            Assert.AreEqual("object", engine.Evaluate("typeof foo"));
            Assert.IsTrue((bool)engine.Evaluate("typeof foo === 'object'"));

            engine.Script.foo = new Hashtable();
            Assert.AreEqual("object", engine.Evaluate("typeof foo"));
            Assert.IsTrue((bool)engine.Evaluate("typeof foo === 'object'"));

            engine.Script.foo = new Queue();
            Assert.AreEqual("object", engine.Evaluate("typeof foo"));
            Assert.IsTrue((bool)engine.Evaluate("typeof foo === 'object'"));

            engine.Script.foo = new SortedList();
            Assert.AreEqual("object", engine.Evaluate("typeof foo"));
            Assert.IsTrue((bool)engine.Evaluate("typeof foo === 'object'"));

            engine.Script.foo = new Stack();
            Assert.AreEqual("object", engine.Evaluate("typeof foo"));
            Assert.IsTrue((bool)engine.Evaluate("typeof foo === 'object'"));

            engine.Script.foo = new List<string>();
            Assert.AreEqual("object", engine.Evaluate("typeof foo"));
            Assert.IsTrue((bool)engine.Evaluate("typeof foo === 'object'"));
            Assert.AreEqual("function", engine.Evaluate("typeof foo.Item"));
            Assert.IsTrue((bool)engine.Evaluate("typeof foo.Item === 'function'"));

            engine.Script.foo = new ExpandoObject();
            engine.Script.host = new HostFunctions();
            Assert.AreEqual("object", engine.Evaluate("typeof foo"));
            Assert.IsTrue((bool)engine.Evaluate("typeof foo === 'object'"));
            Assert.AreEqual("object", engine.Evaluate("typeof host.toStaticType(foo)"));
            Assert.IsTrue((bool)engine.Evaluate("typeof host.toStaticType(foo) === 'object'"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ArrayInvocability()
        {
            engine.Script.foo = Enumerable.Range(123, 5).ToArray();
            Assert.AreEqual(124, engine.Evaluate("foo(1)"));

            engine.Script.foo = new IConvertible[] { "bar" };
            Assert.AreEqual("bar", engine.Evaluate("foo(0)"));

            engine.Script.bar = new List<string>();
            TestUtil.AssertMethodBindException(() => engine.Execute("bar.Add(foo(0))"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_PropertyBagInvocability()
        {
            engine.Script.lib = new HostTypeCollection("mscorlib", "System", "System.Core");
            Assert.IsInstanceOfType(engine.Evaluate("lib('System')"), typeof(PropertyBag));
            Assert.IsInstanceOfType(engine.Evaluate("lib.System('Collections')"), typeof(PropertyBag));
            Assert.IsInstanceOfType(engine.Evaluate("lib('Bogus')"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("lib.System('Heinous')"), typeof(Undefined));

            engine.Script.foo = new PropertyBag { { "Null", null } };
            Assert.IsNull(engine.Evaluate("foo.Null"));
            TestUtil.AssertException<InvalidOperationException>(() => engine.Evaluate("foo.Null(123)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_EnforceAnonymousTypeAccess()
        {
            engine.Script.foo = new { bar = 123, baz = "qux" };
            Assert.AreEqual(123, engine.Evaluate("foo.bar"));
            Assert.AreEqual("qux", engine.Evaluate("foo.baz"));

            engine.EnforceAnonymousTypeAccess = true;
            Assert.IsInstanceOfType(engine.Evaluate("foo.bar"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("foo.baz"), typeof(Undefined));

            engine.AccessContext = GetType();
            Assert.AreEqual(123, engine.Evaluate("foo.bar"));
            Assert.AreEqual("qux", engine.Evaluate("foo.baz"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ScriptObjectMembers()
        {
            engine.Execute(@"
                function Foo() {
                    this.Qux = x => this.Bar = x;
                    this.Xuq = () => this.Baz;
                }
            ");

            var foo = (ScriptObject)engine.Evaluate("new Foo");

            foo.SetProperty("Bar", 123);
            Assert.AreEqual(123, foo.GetProperty("Bar"));

            foo["Baz"] = "abc";
            Assert.AreEqual("abc", foo.GetProperty("Baz"));

            foo.InvokeMethod("Qux", DayOfWeek.Wednesday);
            Assert.AreEqual(DayOfWeek.Wednesday, foo.GetProperty("Bar"));

            foo["Baz"] = BindingFlags.ExactBinding;
            Assert.AreEqual(BindingFlags.ExactBinding, foo.InvokeMethod("Xuq"));

            foo[1] = new HostFunctions();
            Assert.IsInstanceOfType(foo[1], typeof(HostFunctions));
            Assert.IsInstanceOfType(foo[2], typeof(Undefined));

            var names = foo.PropertyNames.ToArray();
            Assert.AreEqual(4, names.Length);
            Assert.IsTrue(names.Contains("Bar"));
            Assert.IsTrue(names.Contains("Baz"));
            Assert.IsTrue(names.Contains("Qux"));
            Assert.IsTrue(names.Contains("Xuq"));

            var indices = foo.PropertyIndices.ToArray();
            Assert.AreEqual(1, indices.Length);
            Assert.IsTrue(indices.Contains(1));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_CpuProfileSampleInterval_Plumbing()
        {
            using (var runtime = new V8Runtime())
            {
                using (var engine1 = runtime.CreateScriptEngine())
                {
                    using (var engine2 = runtime.CreateScriptEngine())
                    {
                        var value = 123456789U;
                        engine1.CpuProfileSampleInterval = value;
                        Assert.AreEqual(value, engine1.CpuProfileSampleInterval);
                        Assert.AreEqual(value, engine2.CpuProfileSampleInterval);
                        Assert.AreEqual(value, runtime.CpuProfileSampleInterval);

                        value = 987654321U;
                        runtime.CpuProfileSampleInterval = value;
                        Assert.AreEqual(value, engine1.CpuProfileSampleInterval);
                        Assert.AreEqual(value, engine2.CpuProfileSampleInterval);
                        Assert.AreEqual(value, runtime.CpuProfileSampleInterval);
                    }
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_CpuProfile()
        {
            const string name = "foo";
            engine.BeginCpuProfile(name, V8CpuProfileFlags.EnableSampleCollection);
            engine.Execute(CreateCpuProfileTestScript());
            var profile = engine.EndCpuProfile(name);

            Assert.AreEqual(engine.Name + ":" + name, profile.Name);
            Assert.IsTrue(profile.StartTimestamp > 0);
            Assert.IsTrue(profile.EndTimestamp > 0);
            Assert.IsNotNull(profile.RootNode);
            Assert.IsNotNull(profile.Samples);
            Assert.IsTrue(profile.Samples.Count > 0);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_CpuProfile_Json()
        {
            const string name = "foo";
            engine.BeginCpuProfile(name, V8CpuProfileFlags.EnableSampleCollection);
            engine.Execute(CreateCpuProfileTestScript());
            var profile = engine.EndCpuProfile(name);

            var json = profile.ToJson();
            var result = JsonConvert.DeserializeObject<JObject>(json);

            Assert.IsInstanceOfType(result["nodes"], typeof(JArray));
            Assert.IsInstanceOfType(result["startTime"], typeof(JValue));
            Assert.IsInstanceOfType(result["endTime"], typeof(JValue));
            Assert.IsInstanceOfType(result["samples"], typeof(JArray));
            Assert.IsInstanceOfType(result["timeDeltas"], typeof(JArray));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ExecuteDocument_Script()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;

            using (var console = new StringWriter())
            {
                var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                engine.AddHostObject("host", new ExtendedHostFunctions());
                engine.AddHostObject("clr", clr);

                engine.ExecuteDocument("JavaScript/General.js");
                Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_EvaluateDocument_Script()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;

            using (var console = new StringWriter())
            {
                var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                engine.AddHostObject("host", new ExtendedHostFunctions());
                engine.AddHostObject("clr", clr);

                Assert.AreEqual((int)Math.Round(Math.Sin(Math.PI) * 1000e16), engine.EvaluateDocument("JavaScript/General.js"));
                Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_CompileDocument_Script()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;

            using (var console = new StringWriter())
            {
                var clr = new HostTypeCollection(type => type != typeof(Console), "mscorlib", "System", "System.Core");
                clr.GetNamespaceNode("System").SetPropertyNoCheck("Console", console);

                engine.AddHostObject("host", new ExtendedHostFunctions());
                engine.AddHostObject("clr", clr);

                var script = engine.CompileDocument("JavaScript/General.js");
                Assert.AreEqual((int)Math.Round(Math.Sin(Math.PI) * 1000e16), engine.Evaluate(script));
                Assert.AreEqual(MiscHelpers.FormatCode(generalScriptOutput), console.ToString().Replace("\r\n", "\n"));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_EvaluateDocument_Module_Standard()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(25 * 25, engine.EvaluateDocument("JavaScript/StandardModule/Module.js", ModuleCategory.Standard));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_CompileDocument_Module_Standard()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;

            var module = engine.CompileDocument("JavaScript/StandardModule/Module.js", ModuleCategory.Standard);
            Assert.AreEqual(25 * 25, engine.Evaluate(module));

            // re-evaluating a module is a no-op
            Assert.IsInstanceOfType(engine.Evaluate(module), typeof(Undefined));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_EvaluateDocument_Module_CommonJS()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            Assert.AreEqual(25 * 25, engine.EvaluateDocument("JavaScript/CommonJS/Module.js", ModuleCategory.CommonJS));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_CompileDocument_Module_CommonJS()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;

            var module = engine.CompileDocument("JavaScript/CommonJS/Module.js", ModuleCategory.CommonJS);
            Assert.AreEqual(25 * 25, engine.Evaluate(module));

            // re-evaluating a module is a no-op
            Assert.IsInstanceOfType(engine.Evaluate(module), typeof(Undefined));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DocumentSettings_EnforceRelativePrefix()
        {
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading | DocumentAccessFlags.EnforceRelativePrefix;
            TestUtil.AssertException<FileNotFoundException>(() => engine.EvaluateDocument("JavaScript/CommonJS/Module.js", ModuleCategory.CommonJS));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ScriptCaching()
        {
            Assert.AreEqual(1UL, engine.GetRuntimeStatistics().ScriptCount);

            Assert.AreEqual(Math.PI, engine.Evaluate("Math.PI"));
            Assert.AreEqual(Math.PI, engine.Evaluate("Math.PI"));
            Assert.AreEqual(3UL, engine.GetRuntimeStatistics().ScriptCount);

            var info = new DocumentInfo("Test");

            Assert.AreEqual(Math.E, engine.Evaluate(info, "Math.E"));
            Assert.AreEqual(Math.E, engine.Evaluate(info, "Math.E"));
            Assert.AreEqual(4UL, engine.GetRuntimeStatistics().ScriptCount);

            Assert.AreEqual(Math.PI, engine.Evaluate(info, "Math.PI"));
            Assert.AreEqual(Math.PI, engine.Evaluate(info, "Math.PI"));
            Assert.AreEqual(5UL, engine.GetRuntimeStatistics().ScriptCount);

            using (var runtime = new V8Runtime())
            {
                for (var i = 0; i < 10; i++)
                {
                    using (var testEngine = runtime.CreateScriptEngine())
                    {
                        Assert.AreEqual(Math.PI, testEngine.Evaluate(info, "Math.PI"));
                        Assert.AreEqual(Math.E, testEngine.Evaluate(info, "Math.E"));
                        Assert.AreEqual((i < 1) ? 3UL : 0UL, testEngine.GetStatistics().ScriptCount);
                    }
                }

                Assert.AreEqual(3UL, runtime.GetStatistics().ScriptCount);
            }

            using (var runtime = new V8Runtime())
            {
                for (var i = 0; i < 1100; i++)
                {
                    using (var testEngine = runtime.CreateScriptEngine())
                    {
                        Assert.AreEqual(Math.PI + i, testEngine.Evaluate(info, "Math.PI" + "+" + i));
                    }
                }

                Assert.AreEqual(1101UL, runtime.GetStatistics().ScriptCount);
                Assert.AreEqual(1024UL, runtime.GetStatistics().ScriptCacheSize);
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ScriptArray_IList()
        {
            var array = (IList)engine.Evaluate("array = []");
            Assert.AreEqual(0, array.Add(123));
            Assert.AreEqual(1, array.Add(456.789));
            Assert.AreEqual(2, array.Add("foo"));
            Assert.AreEqual(3, array.Add(engine.Evaluate("({ bar: 'baz' })")));

            IDictionary<string, object> expando = new ExpandoObject();
            expando["qux"] = "quux";
            Assert.AreEqual(4, array.Add(expando));

            Assert.AreEqual(5, array.Count);
            Assert.AreEqual(5, engine.Evaluate("array.length"));
            Assert.AreEqual("[123,456.789,\"foo\",{\"bar\":\"baz\"},{\"qux\":\"quux\"}]", JsonConvert.SerializeObject(engine.Evaluate("array")));

            var copy = new object[5];
            array.CopyTo(copy, 0);
            Assert.AreEqual(123, copy[0]);
            Assert.AreEqual(456.789, copy[1]);
            Assert.AreEqual("foo", copy[2]);
            Assert.AreEqual("baz", ((dynamic)copy[3]).bar);
            Assert.AreEqual("quux", ((dynamic)copy[4]).qux);

            Assert.IsTrue(array.Contains(456.789));
            Assert.IsFalse(array.Contains(Math.PI));

            Assert.AreEqual(1, array.IndexOf(456.789));
            Assert.AreEqual(-1, array.IndexOf(Math.PI));

            array.Insert(2, 123456);
            Assert.AreEqual(6, array.Count);
            Assert.AreEqual(6, engine.Evaluate("array.length"));
            Assert.AreEqual("[123,456.789,123456,\"foo\",{\"bar\":\"baz\"},{\"qux\":\"quux\"}]", JsonConvert.SerializeObject(engine.Evaluate("array")));

            array.Remove(123456);
            Assert.AreEqual(5, array.Count);
            Assert.AreEqual(5, engine.Evaluate("array.length"));
            Assert.AreEqual("[123,456.789,\"foo\",{\"bar\":\"baz\"},{\"qux\":\"quux\"}]", JsonConvert.SerializeObject(engine.Evaluate("array")));

            array.RemoveAt(1);
            Assert.AreEqual(4, array.Count);
            Assert.AreEqual(4, engine.Evaluate("array.length"));
            Assert.AreEqual("[123,\"foo\",{\"bar\":\"baz\"},{\"qux\":\"quux\"}]", JsonConvert.SerializeObject(engine.Evaluate("array")));

            array.Clear();
            Assert.AreEqual(0, array.Count);
            Assert.AreEqual(0, engine.Evaluate("array.length"));
            Assert.AreEqual("[]", JsonConvert.SerializeObject(engine.Evaluate("array")));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ScriptObject_IDictionary()
        {
            // ReSharper disable UsageOfDefaultStructEquality

            var pairs = new List<KeyValuePair<string, object>>
            {
                new("123", 987),
                new("456", 654.321),
                new("abc", 123),
                new("def", 456.789),
                new("ghi", "foo"),
                new("jkl", engine.Evaluate("({ bar: 'baz' })"))
            };

            var dict = (IDictionary<string, object>)engine.Evaluate("dict = {}");

            pairs.ForEach(pair => dict.Add(pair));
            Assert.IsTrue(dict.SequenceEqual(pairs));

            var index = 0;
            foreach (var pair in dict)
            {
                Assert.AreEqual(pairs[index++], pair);
            }

            index = 0;
            foreach (var pair in (IEnumerable)dict)
            {
                Assert.AreEqual(pairs[index++], pair);
            }

            dict.Clear();
            Assert.AreEqual(0, dict.Count);

            pairs.ForEach(pair => dict.Add(pair.Key, pair.Value));
            Assert.IsTrue(dict.SequenceEqual(pairs));

            Assert.IsTrue(pairs.All(pair => dict.Contains(pair)));
            Assert.IsTrue(pairs.All(pair => dict.ContainsKey(pair.Key)));

            var testPairs = new KeyValuePair<string, object>[pairs.Count + 3];
            dict.CopyTo(testPairs, 3);
            Assert.IsTrue(testPairs.Skip(3).SequenceEqual(pairs));

            Assert.IsTrue(pairs.All(pair => dict.Remove(pair)));
            Assert.AreEqual(0, dict.Count);

            pairs.ForEach(pair => dict.Add(pair.Key, pair.Value));
            Assert.IsTrue(dict.SequenceEqual(pairs));

            Assert.IsTrue(pairs.All(pair => dict.Remove(pair.Key)));
            Assert.AreEqual(0, dict.Count);

            pairs.ForEach(pair => dict.Add(pair.Key, pair.Value));
            Assert.IsTrue(dict.SequenceEqual(pairs));

            Assert.IsTrue(pairs.All(pair => dict.TryGetValue(pair.Key, out var value) && Equals(value, pair.Value)));
            Assert.IsTrue(pairs.All(pair => Equals(dict[pair.Key], pair.Value)));

            Assert.IsTrue(pairs.Select(pair => pair.Key).SequenceEqual(dict.Keys));
            Assert.IsTrue(pairs.Select(pair => pair.Value).SequenceEqual(dict.Values));

            Assert.IsFalse(dict.TryGetValue("qux", out _));
            TestUtil.AssertException<KeyNotFoundException>(() => Assert.IsTrue(dict["qux"] is Undefined));

            engine.Execute("dict[789] = Math.PI");
            Assert.IsTrue(dict.TryGetValue("789", out var pi) && Equals(pi, Math.PI));
            Assert.IsFalse(pairs.SequenceEqual(dict));

            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("delete dict[789]")));
            Assert.IsTrue(pairs.SequenceEqual(dict));

            // ReSharper restore UsageOfDefaultStructEquality
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_UndefinedImportValue()
        {
            Assert.IsNull(engine.Evaluate("null"));
            Assert.IsInstanceOfType(engine.Evaluate("undefined"), typeof(Undefined));

            engine.UndefinedImportValue = null;
            Assert.IsNull(engine.Evaluate("null"));
            Assert.IsNull(engine.Evaluate("undefined"));

            engine.UndefinedImportValue = 123;
            Assert.IsNull(engine.Evaluate("null"));
            Assert.AreEqual(123, engine.Evaluate("undefined"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_NullImportValue()
        {
            Assert.IsNull(engine.Evaluate("null"));
            Assert.IsInstanceOfType(engine.Evaluate("undefined"), typeof(Undefined));

            engine.NullImportValue = Undefined.Value;
            Assert.IsInstanceOfType(engine.Evaluate("null"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("undefined"), typeof(Undefined));

            engine.NullImportValue = 123;
            Assert.AreEqual(123, engine.Evaluate("null"));
            Assert.IsInstanceOfType(engine.Evaluate("undefined"), typeof(Undefined));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_NullExportValue()
        {
            engine.Script.foo = new Func<object>(() => null);
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("foo() === null")));

            engine.NullExportValue = Undefined.Value;
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("foo() === undefined")));

            engine.NullExportValue = null;
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("foo() === null")));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_VoidResultValue()
        {
            engine.Script.foo = new Action(() => {});
            Assert.IsInstanceOfType(engine.Evaluate("foo()"), typeof(VoidResult));

            engine.VoidResultValue = 123;
            Assert.AreEqual(123, engine.Evaluate("foo()"));

            engine.VoidResultValue = Undefined.Value;
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("typeof(foo()) === 'undefined'")));

            engine.VoidResultValue = VoidResult.Value;
            Assert.IsInstanceOfType(engine.Evaluate("foo()"), typeof(VoidResult));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ExposeStaticMembersOnHostObjects()
        {
            engine.Script.utf8 = Encoding.UTF8;
            Assert.AreEqual("utf-8", engine.Evaluate("utf8.WebName"));
            Assert.IsInstanceOfType(engine.Evaluate("utf8.ASCII"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("utf8.ReferenceEquals"), typeof(Undefined));

            engine.ExposeHostObjectStaticMembers = true;
            Assert.AreEqual("utf-8", engine.Evaluate("utf8.WebName"));
            Assert.IsInstanceOfType(engine.Evaluate("utf8.ASCII"), typeof(Encoding));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("utf8.ReferenceEquals(null, null)")));

            engine.ExposeHostObjectStaticMembers = false;
            Assert.AreEqual("utf-8", engine.Evaluate("utf8.WebName"));
            Assert.IsInstanceOfType(engine.Evaluate("utf8.ASCII"), typeof(Undefined));
            Assert.IsInstanceOfType(engine.Evaluate("utf8.ReferenceEquals"), typeof(Undefined));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_BigInt()
        {
            TestBigInt(0);
            TestBigInt(int.MinValue);
            TestBigInt(int.MaxValue);
            TestBigInt(uint.MinValue);
            TestBigInt(uint.MaxValue);
            TestBigInt(long.MinValue);
            TestBigInt(long.MaxValue);
            TestBigInt(ulong.MinValue);
            TestBigInt(ulong.MaxValue);
            TestBigInt(new BigInteger(float.MinValue));
            TestBigInt(new BigInteger(float.MaxValue));
            TestBigInt(new BigInteger(decimal.MinValue));
            TestBigInt(new BigInteger(decimal.MaxValue));

            var random = new Random();
            var length = random.Next(32, 65);
            var bytes = new byte[length];
            random.NextBytes(bytes);

            bytes[length - 1] &= 0x7F;
            var value = new BigInteger(bytes);
            Assert.IsTrue(value >= 0);
            TestBigInt(value);

            bytes[length - 1] |= 0x80;
            value = new BigInteger(bytes);
            Assert.IsTrue(value < 0);
            TestBigInt(value);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_BigInt_NoInt64Conversion()
        {
            engine.Script.value = MiscHelpers.MaxInt64InDouble;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));
            engine.Script.value = MiscHelpers.MaxInt64InDouble + 1;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));

            engine.Script.value = -MiscHelpers.MaxInt64InDouble;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));
            engine.Script.value = -MiscHelpers.MaxInt64InDouble - 1;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));

            engine.Script.value = (ulong)MiscHelpers.MaxInt64InDouble;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));
            engine.Script.value = (ulong)MiscHelpers.MaxInt64InDouble + 1;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));

            engine.Script.value = int.MinValue;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));
            engine.Script.value = uint.MaxValue;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_BigInt_UnsafeInt64Conversion()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.MarshalUnsafeInt64AsBigInt);

            engine.Script.value = MiscHelpers.MaxInt64InDouble;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));
            engine.Script.value = MiscHelpers.MaxInt64InDouble + 1;
            Assert.AreEqual("bigint", engine.Evaluate("typeof value"));

            engine.Script.value = -MiscHelpers.MaxInt64InDouble;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));
            engine.Script.value = -MiscHelpers.MaxInt64InDouble - 1;
            Assert.AreEqual("bigint", engine.Evaluate("typeof value"));

            engine.Script.value = (ulong)MiscHelpers.MaxInt64InDouble;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));
            engine.Script.value = (ulong)MiscHelpers.MaxInt64InDouble + 1;
            Assert.AreEqual("bigint", engine.Evaluate("typeof value"));

            engine.Script.value = int.MinValue;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));
            engine.Script.value = uint.MaxValue;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_BigInt_AllInt64Conversion()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.MarshalAllInt64AsBigInt);

            engine.Script.value = MiscHelpers.MaxInt64InDouble;
            Assert.AreEqual("bigint", engine.Evaluate("typeof value"));
            engine.Script.value = MiscHelpers.MaxInt64InDouble + 1;
            Assert.AreEqual("bigint", engine.Evaluate("typeof value"));

            engine.Script.value = -MiscHelpers.MaxInt64InDouble;
            Assert.AreEqual("bigint", engine.Evaluate("typeof value"));
            engine.Script.value = -MiscHelpers.MaxInt64InDouble - 1;
            Assert.AreEqual("bigint", engine.Evaluate("typeof value"));

            engine.Script.value = (ulong)MiscHelpers.MaxInt64InDouble;
            Assert.AreEqual("bigint", engine.Evaluate("typeof value"));
            engine.Script.value = (ulong)MiscHelpers.MaxInt64InDouble + 1;
            Assert.AreEqual("bigint", engine.Evaluate("typeof value"));

            engine.Script.value = int.MinValue;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));
            engine.Script.value = uint.MaxValue;
            Assert.AreEqual("number", engine.Evaluate("typeof value"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_isPromise()
        {
            engine.Execute("value = new Promise(() => {})");
            Assert.IsTrue(engine.Script.EngineInternal.isPromise(engine.Script.value));

            engine.Execute("delete Promise");
            Assert.IsInstanceOfType(engine.Script.Promise, typeof(Undefined));

            engine.Execute("function Promise() { this.foo = 123; } value2 = new Promise();");
            Assert.AreEqual(123, engine.Script.value2.foo);
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("value2 instanceof Promise")));

            Assert.IsFalse(engine.Script.EngineInternal.isPromise(engine.Script.value2));
            Assert.IsTrue(engine.Script.EngineInternal.isPromise(engine.Script.value));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_I18n()
        {
            Assert.AreEqual(1, engine.Evaluate("'a'.localeCompare('A', 'en', { 'caseFirst': 'upper'})"));
            Assert.AreEqual(-1, engine.Evaluate("'a'.localeCompare('A', 'en', { 'caseFirst': 'lower'})"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_WriteRuntimeHeapSnapshot()
        {
            using (var stream = new MemoryStream())
            {
                engine.WriteRuntimeHeapSnapshot(stream);
                stream.Seek(0, SeekOrigin.Begin);
                using (var reader = new StreamReader(stream, Encoding.ASCII))
                {
                    var snapshot = JObject.Parse(reader.ReadToEnd());
                    Assert.AreEqual(JTokenType.Object, snapshot["snapshot"].Type);
                    Assert.AreEqual(JTokenType.Array, snapshot["nodes"].Type);
                    Assert.AreEqual(JTokenType.Array, snapshot["edges"].Type);
                    Assert.AreEqual(JTokenType.Array, snapshot["strings"].Type);
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_WriteRuntimeHeapSnapshot_StreamError()
        {
            TestUtil.AssertException<NotSupportedException>(() =>
            {
                using (var stream = new MemoryStream(new byte[100]))
                {
                    engine.WriteRuntimeHeapSnapshot(stream);
                }
            });
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Runtime_WriteHeapSnapshot()
        {
            using (var runtime = new V8Runtime(V8RuntimeFlags.EnableDebugging))
            {
                using (var stream = new MemoryStream())
                {
                    runtime.WriteHeapSnapshot(stream);
                    stream.Seek(0, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream, Encoding.ASCII))
                    {
                        var snapshot = JObject.Parse(reader.ReadToEnd());
                        Assert.AreEqual(JTokenType.Object, snapshot["snapshot"].Type);
                        Assert.AreEqual(JTokenType.Array, snapshot["nodes"].Type);
                        Assert.AreEqual(JTokenType.Array, snapshot["edges"].Type);
                        Assert.AreEqual(JTokenType.Array, snapshot["strings"].Type);
                    }
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Runtime_WriteHeapSnapshot_StreamError()
        {
            using (var runtime = new V8Runtime(V8RuntimeFlags.EnableDebugging))
            {
                TestUtil.AssertException<NotSupportedException>(() =>
                {
                    using (var stream = new MemoryStream(new byte[100]))
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        runtime.WriteHeapSnapshot(stream);
                    }
                });
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DisableExtensionMethods()
        {
            engine.AddHostType(typeof(Enumerable));
            engine.AddHostType("Filter", typeof(Func<int, bool>));
            engine.Script.array = Enumerable.Range(0, 10).ToArray();

            Assert.IsFalse(engine.DisableExtensionMethods);
            Assert.IsFalse(engine.Evaluate("array.Where") is Undefined);
            Assert.AreEqual("1,3,5,7,9", engine.Evaluate("Array.from(array.Where(new Filter(n => (n & 1) === 1))).toString()"));

            engine.DisableExtensionMethods = true;
            Assert.IsTrue(engine.DisableExtensionMethods);
            Assert.IsTrue(engine.Evaluate("array.Where") is Undefined);
            TestUtil.AssertException<ScriptEngineException>(() => engine.Evaluate("Array.from(array.Where(new Filter(n => (n & 1) === 1))).toString()"));

            engine.DisableExtensionMethods = false;
            Assert.IsFalse(engine.DisableExtensionMethods);
            Assert.IsFalse(engine.Evaluate("array.Where") is Undefined);
            Assert.AreEqual("1,3,5,7,9", engine.Evaluate("Array.from(array.Where(new Filter(n => (n & 1) === 1))).toString()"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_MaxArrayBufferAllocation()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(new V8RuntimeConstraints { MaxArrayBufferAllocation = 8192 }, V8ScriptEngineFlags.EnableDebugging);

            engine.Execute("a1 = new Uint8Array(8192)");
            TestUtil.AssertException<ScriptEngineException>(() => engine.Execute("a2 = new Uint8Array(8192)"));

            engine.Execute("a1 = null");
            engine.CollectGarbage(true);

            engine.Execute("a1 = new Int16Array(2048)");
            engine.Execute("a2 = new Int16Array(2048)");
            TestUtil.AssertException<ScriptEngineException>(() => engine.Execute("a3 = new Int16Array(2048)"));

            engine.Execute("a1 = null");
            engine.Execute("a2 = null");
            engine.CollectGarbage(true);

            engine.Execute("a1 = new Float64Array(1024)");
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DisableFloatNarrowing()
        {
            engine.AddHostType("StringT", typeof(string));
            Assert.AreEqual("123,456.80", engine.Evaluate("StringT.Format('{0:###,###.00}', 123456.75)"));
            engine.DisableFloatNarrowing = true;
            Assert.AreEqual("123,456.75", engine.Evaluate("StringT.Format('{0:###,###.00}', 123456.75)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_CancelAwaitDebugger()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.AwaitDebuggerAndPauseOnStart);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(1000);
                engine.CancelAwaitDebugger();
            });

            engine.Execute("foo = Math.E * Math.PI");
            Assert.AreEqual(Math.E * Math.PI, engine.Script.foo);
            Assert.AreEqual(Math.Sqrt(Math.E * Math.PI), engine.Evaluate("Math.sqrt(Math.E * Math.PI)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ForeignScriptObject_DirectAccess()
        {
            using (var runtime = new V8Runtime())
            {
                using (var foreignEngine = runtime.CreateScriptEngine())
                {
                    engine.Script.foreignFunction = foreignEngine.Script.Function;
                    Assert.AreEqual("object", engine.Evaluate("typeof foreignFunction"));
                    Assert.AreEqual("function Function() { [native code] }", engine.Evaluate("foreignFunction.toString()"));

                    engine.Dispose();
                    engine = runtime.CreateScriptEngine();

                    engine.Script.foreignFunction = foreignEngine.Script.Function;
                    Assert.AreEqual("function", engine.Evaluate("typeof foreignFunction"));
                    Assert.AreEqual("function Function() { [native code] }", engine.Evaluate("foreignFunction.toString()"));
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ForeignScriptObject_DirectAccess_DisposedEngine()
        {
            using (var runtime = new V8Runtime())
            {
                object function;
                using (var foreignEngine = runtime.CreateScriptEngine())
                {
                    function = foreignEngine.Script.Function;
                }

                engine.Dispose();
                engine = runtime.CreateScriptEngine();

                engine.Script.foreignFunction = function;
                Assert.AreEqual("function", engine.Evaluate("typeof foreignFunction"));
                Assert.AreEqual("function Function() { [native code] }", engine.Evaluate("foreignFunction.toString()"));
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_EnableRuntimeInterruptPropagation_Plumbing()
        {
            using (var runtime = new V8Runtime())
            {
                using (var engine1 = runtime.CreateScriptEngine())
                {
                    using (var engine2 = runtime.CreateScriptEngine())
                    {
                        Assert.IsFalse(runtime.EnableInterruptPropagation);
                        Assert.IsFalse(engine1.EnableRuntimeInterruptPropagation);
                        Assert.IsFalse(engine2.EnableRuntimeInterruptPropagation);

                        runtime.EnableInterruptPropagation = true;
                        Assert.IsTrue(runtime.EnableInterruptPropagation);
                        Assert.IsTrue(engine1.EnableRuntimeInterruptPropagation);
                        Assert.IsTrue(engine2.EnableRuntimeInterruptPropagation);

                        engine1.EnableRuntimeInterruptPropagation = false;
                        Assert.IsFalse(runtime.EnableInterruptPropagation);
                        Assert.IsFalse(engine1.EnableRuntimeInterruptPropagation);
                        Assert.IsFalse(engine2.EnableRuntimeInterruptPropagation);
                    }
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_EnableRuntimeInterruptPropagation()
        {
            var interrupt = new Action(() =>
            {
                engine.Interrupt();
                engine.Execute("while (true);");
            });

            var code = @"
                try {
                    interrupt();
                }
                catch {
                }
                123
            ";

            engine.Script.interrupt = interrupt;
            Assert.AreEqual(123, engine.Evaluate(code));

            engine.EnableRuntimeInterruptPropagation = true;
            TestUtil.AssertException<ScriptInterruptedException>(() => engine.Evaluate(code));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_EnableRuntimeInterruptPropagation_CancelInterrupt()
        {
            var catchAndCancel = false;

            var interrupt = new Action(() =>
            {
                // ReSharper disable AccessToModifiedClosure

                try
                {
                    engine.Interrupt();
                    engine.Execute("while (true);");
                }
                catch (ScriptInterruptedException) when (catchAndCancel)
                {
                    engine.CancelInterrupt();
                }

                // ReSharper restore AccessToModifiedClosure
            });

            var code = @"
                try {
                    interrupt();
                }
                catch {
                }
                123
            ";

            engine.Script.interrupt = interrupt;
            Assert.AreEqual(123, engine.Evaluate(code));

            engine.EnableRuntimeInterruptPropagation = true;
            TestUtil.AssertException<ScriptInterruptedException>(() => engine.Evaluate(code));

            catchAndCancel = true;
            Assert.AreEqual(123, engine.Evaluate(code));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_RuntimeHeapSizeViolationPolicy_Plumbing()
        {
            using (var runtime = new V8Runtime())
            {
                using (var engine1 = runtime.CreateScriptEngine())
                {
                    using (var engine2 = runtime.CreateScriptEngine())
                    {
                        Assert.AreEqual(V8RuntimeViolationPolicy.Interrupt, runtime.HeapSizeViolationPolicy);
                        Assert.AreEqual(V8RuntimeViolationPolicy.Interrupt, engine1.RuntimeHeapSizeViolationPolicy);
                        Assert.AreEqual(V8RuntimeViolationPolicy.Interrupt, engine2.RuntimeHeapSizeViolationPolicy);

                        runtime.HeapSizeViolationPolicy = V8RuntimeViolationPolicy.Exception;
                        Assert.AreEqual(V8RuntimeViolationPolicy.Exception, runtime.HeapSizeViolationPolicy);
                        Assert.AreEqual(V8RuntimeViolationPolicy.Exception, engine1.RuntimeHeapSizeViolationPolicy);
                        Assert.AreEqual(V8RuntimeViolationPolicy.Exception, engine2.RuntimeHeapSizeViolationPolicy);

                        engine1.RuntimeHeapSizeViolationPolicy = V8RuntimeViolationPolicy.Interrupt;
                        Assert.AreEqual(V8RuntimeViolationPolicy.Interrupt, runtime.HeapSizeViolationPolicy);
                        Assert.AreEqual(V8RuntimeViolationPolicy.Interrupt, engine1.RuntimeHeapSizeViolationPolicy);
                        Assert.AreEqual(V8RuntimeViolationPolicy.Interrupt, engine2.RuntimeHeapSizeViolationPolicy);
                    }
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_RuntimeHeapSizeViolationPolicy()
        {
            const int limit = 4 * 1024 * 1024;
            const string code = @"
                x = {};
                for (var i = 0; true; ++i) {
                    x = { next: x };
                    if ((i % 20000) === 0) EngineInternal.checkpoint();
                }
            ";

            engine.MaxRuntimeHeapSize = (UIntPtr)limit;
            engine.RuntimeHeapSizeViolationPolicy = V8RuntimeViolationPolicy.Interrupt;

            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.Execute(code);
                }
                catch (ScriptEngineException exception)
                {
                    Assert.IsTrue(exception.IsFatal);
                    Assert.IsNull(exception.ScriptExceptionAsObject);
                    Assert.AreEqual("The V8 runtime has exceeded its memory limit", exception.Message);
                    throw;
                }
            });

            Assert.AreEqual((UIntPtr)limit, engine.MaxRuntimeHeapSize);

            engine.MaxRuntimeHeapSize = UIntPtr.Zero;
            engine.Execute("delete x");
            engine.CollectGarbage(true);

            engine.MaxRuntimeHeapSize = (UIntPtr)limit;
            engine.RuntimeHeapSizeViolationPolicy = V8RuntimeViolationPolicy.Exception;

            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                try
                {
                    engine.Execute(code);
                }
                catch (ScriptEngineException exception)
                {
                    Assert.IsFalse(exception.IsFatal);
                    Assert.IsNotNull(exception.ScriptExceptionAsObject);
                    Assert.AreEqual("Error: The V8 runtime has exceeded its memory limit", exception.Message);
                    throw;
                }
            });

            Assert.AreEqual(UIntPtr.Zero, engine.MaxRuntimeHeapSize);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_RuntimeHeapSizeViolationPolicy_Async()
        {
            const int limit = 4 * 1024 * 1024;
            const string code = @"(async function() {
                x = {};
                for (var i = 0; true; ++i) {
                    x = { next: x };
                    if ((i % 20000) === 0) EngineInternal.checkpoint();
                }
            })()";

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableTaskPromiseConversion);

            engine.MaxRuntimeHeapSize = (UIntPtr)limit;
            engine.RuntimeHeapSizeViolationPolicy = V8RuntimeViolationPolicy.Interrupt;

            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                Func<Task> test = async () =>
                {
                    try
                    {
                        await (Task)engine.Evaluate(code);
                    }
                    catch (ScriptEngineException exception)
                    {
                        Assert.IsTrue(exception.IsFatal);
                        Assert.IsNull(exception.ScriptExceptionAsObject);
                        Assert.AreEqual("The V8 runtime has exceeded its memory limit", exception.Message);
                        throw;
                    }
                };

                test().Wait();
            });

            Assert.AreEqual((UIntPtr)limit, engine.MaxRuntimeHeapSize);

            engine.MaxRuntimeHeapSize = UIntPtr.Zero;
            engine.Execute("delete x");
            engine.CollectGarbage(true);

            engine.MaxRuntimeHeapSize = (UIntPtr)limit;
            engine.RuntimeHeapSizeViolationPolicy = V8RuntimeViolationPolicy.Exception;

            TestUtil.AssertException<ScriptEngineException>(() =>
            {
                Func<Task> test = async () =>
                {
                    try
                    {
                        await (Task)engine.Evaluate(code);
                    }
                    catch (ScriptEngineException exception)
                    {
                        Assert.IsFalse(exception.IsFatal);
                        Assert.IsNotNull(exception.ScriptExceptionAsObject);
                        Assert.AreEqual("Error: The V8 runtime has exceeded its memory limit", exception.Message);
                        throw;
                    }
                };

                test().Wait();
            });

            Assert.AreEqual(UIntPtr.Zero, engine.MaxRuntimeHeapSize);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_WebAssembly()
        {
            dynamic setup = engine.Evaluate(@"
                (function (dirPath, readFile) {
                    return new Promise((resolve, reject) => {
                        globalThis.console = {
                            log: text => resolve(text),
                            warn: text => reject(text)
                        };
                        globalThis.process = {
                            versions: { node: '14.0' },
                            argv: [],
                            on: () => {}
                        };
                        globalThis.require = function (name) {
                            if (name == 'fs') return { readFileSync: path => readFile(path) };
                            if (name == 'path') return { normalize: path => path };
                        };
                        globalThis.__dirname = dirPath;
                    });
                })
            ");

            var dirPath = Path.Combine(Directory.GetCurrentDirectory(), "JavaScript", "WebAssembly");
            var readFile = new Func<string, object>(path =>
            {
                // ReSharper disable AccessToDisposedClosure

                var bytes = File.ReadAllBytes(path);
                ScriptObject uint8ArrayClass = engine.Script.Uint8Array;
                var typedArray = (ITypedArray<byte>)uint8ArrayClass.Invoke(true, bytes.Length);
                typedArray.WriteBytes(bytes, 0, Convert.ToUInt64(bytes.Length), 0);
                return typedArray;

                // ReSharper restore AccessToDisposedClosure
            });

            var task = ((object)setup(dirPath, readFile)).ToTask();

            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            engine.ExecuteDocument("JavaScript/WebAssembly/HelloWorld.js");

            Assert.AreEqual("hello, world!", task.Result);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_CaseInsensitiveMemberBinding()
        {
            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding);

            TestCamelCaseMemberBinding();
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_CustomAttributeLoader()
        {
            using (Scope.Create(() => HostSettings.CustomAttributeLoader, loader => HostSettings.CustomAttributeLoader = loader))
            {
                HostSettings.CustomAttributeLoader = new CamelCaseAttributeLoader();
                TestCamelCaseMemberBinding();
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_CustomAttributeLoader_Private()
        {
            using (var otherEngine = new V8ScriptEngine())
            {
                engine.CustomAttributeLoader = new CamelCaseAttributeLoader();
                TestCamelCaseMemberBinding();

                using (Scope.Create(() => engine, originalEngine => engine = originalEngine))
                {
                    engine = otherEngine;
                    TestUtil.AssertException<InvalidCastException>(TestCamelCaseMemberBinding);
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_StringifyEnhancements()
        {
            engine.Script.hostObject = new Dictionary<string, object> { { "foo", 123 }, { "bar", "baz" }, { "qux", engine.Evaluate("({ quux: 456.789, quuz: 'corge' })") } };
            Assert.IsInstanceOfType(engine.Evaluate("JSON.stringify(hostObject)"), typeof(Undefined));

            engine.Execute("scriptObject = { grault: null, garply: hostObject }");
            Assert.AreEqual("{\"grault\":null}", engine.Evaluate("JSON.stringify(scriptObject)"));

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableStringifyEnhancements);

            engine.Script.hostObject = new Dictionary<string, object> { { "foo", 123 }, { "bar", "baz" }, { "qux", engine.Evaluate("({ quux: 456.789, quuz: 'corge' })") } };
            Assert.AreEqual("{\"foo\":123,\"bar\":\"baz\",\"qux\":{\"quux\":456.789,\"quuz\":\"corge\"}}", engine.Evaluate("JSON.stringify(hostObject)"));

            engine.Execute("scriptObject = { grault: null, garply: hostObject }");
            Assert.AreEqual("{\"grault\":null,\"garply\":{\"foo\":123,\"bar\":\"baz\",\"qux\":{\"quux\":456.789,\"quuz\":\"corge\"}}}", engine.Evaluate("JSON.stringify(scriptObject)"));

            engine.Execute("hostObject.Add('jerry', scriptObject)");
            TestUtil.AssertException<InvalidOperationException>(() => engine.Evaluate("JSON.stringify(hostObject)"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_TotalExternalSize()
        {
            var size = engine.GetRuntimeHeapInfo().TotalExternalSize;
            engine.Execute("arr = new Uint8Array(1234567)");
            Assert.AreEqual(size + 1234567UL, engine.GetRuntimeHeapInfo().TotalExternalSize);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Runtime_TotalExternalSize()
        {
            using (var runtime = new V8Runtime())
            {
                using (var tempEngine = runtime.CreateScriptEngine())
                {
                    var size = runtime.GetHeapInfo().TotalExternalSize;
                    tempEngine.Execute("arr = new Uint8Array(7654321)");
                    Assert.AreEqual(size + 7654321UL, runtime.GetHeapInfo().TotalExternalSize);
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ScriptObjectIdentity()
        {
            var list = new List<object>();
            engine.Script.list = list;

            engine.Execute(@"
                obj = {};
                list.Add(obj);
                func = () => {};
                list.Add(func);
            ");

            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(engine.Script.obj, list[0]);
            Assert.AreEqual(engine.Script.func, list[1]);

            Assert.AreEqual(true, engine.Evaluate("list.Remove(obj)"));
            Assert.AreEqual(false, engine.Evaluate("list.Remove(obj)"));

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(engine.Script.func, list[0]);

            Assert.AreEqual(true, engine.Evaluate("list.Remove(func)"));
            Assert.AreEqual(false, engine.Evaluate("list.Remove(func)"));

            Assert.AreEqual(0, list.Count);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_JavaScriptObjectKindAndFlags()
        {
            (JavaScriptObjectKind, JavaScriptObjectFlags) Inspect(string expression)
            {
                var obj = (IJavaScriptObject)engine.Evaluate($"({expression})");
                return (obj.Kind, obj.Flags);
            }

            Assert.AreEqual((JavaScriptObjectKind.Unknown, JavaScriptObjectFlags.None), Inspect("{}"));
            Assert.AreEqual((JavaScriptObjectKind.Promise, JavaScriptObjectFlags.None), Inspect("(async function () {})()"));
            Assert.AreEqual((JavaScriptObjectKind.Array, JavaScriptObjectFlags.None), Inspect("[]"));

            Assert.AreEqual((JavaScriptObjectKind.Function, JavaScriptObjectFlags.None), Inspect("function () {}"));
            Assert.AreEqual((JavaScriptObjectKind.Function, JavaScriptObjectFlags.Async), Inspect("async function () {}"));
            Assert.AreEqual((JavaScriptObjectKind.Function, JavaScriptObjectFlags.Generator), Inspect("function* () {}"));
            Assert.AreEqual((JavaScriptObjectKind.Function, JavaScriptObjectFlags.Async | JavaScriptObjectFlags.Generator), Inspect("async function* () {}"));

            Assert.AreEqual((JavaScriptObjectKind.Iterator, JavaScriptObjectFlags.None), Inspect("(function* () {})()"));
            Assert.AreEqual((JavaScriptObjectKind.Iterator, JavaScriptObjectFlags.Async), Inspect("(async function* () {})()"));

            engine.Script.list = new List<int>();
            Assert.AreEqual((JavaScriptObjectKind.Iterator, JavaScriptObjectFlags.None), Inspect("(list[Symbol.iterator])()"));
            Assert.AreEqual((JavaScriptObjectKind.Iterator, JavaScriptObjectFlags.Async), Inspect("(list[Symbol.asyncIterator])()"));

            Assert.AreEqual((JavaScriptObjectKind.ArrayBuffer, JavaScriptObjectFlags.None), Inspect("new ArrayBuffer(256)"));
            Assert.AreEqual((JavaScriptObjectKind.ArrayBuffer, JavaScriptObjectFlags.Shared), Inspect("new SharedArrayBuffer(256)"));

            Assert.AreEqual((JavaScriptObjectKind.DataView, JavaScriptObjectFlags.None), Inspect("new DataView(new ArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.DataView, JavaScriptObjectFlags.Shared), Inspect("new DataView(new SharedArrayBuffer(256))"));

            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.None), Inspect("new Uint8Array(new ArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.None), Inspect("new Uint8ClampedArray(new ArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.None), Inspect("new Int8Array(new ArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.None), Inspect("new Uint16Array(new ArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.None), Inspect("new Int16Array(new ArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.None), Inspect("new Uint32Array(new ArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.None), Inspect("new Int32Array(new ArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.None), Inspect("new BigUint64Array(new ArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.None), Inspect("new BigInt64Array(new ArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.None), Inspect("new Float32Array(new ArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.None), Inspect("new Float64Array(new ArrayBuffer(256))"));

            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.Shared), Inspect("new Uint8Array(new SharedArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.Shared), Inspect("new Uint8ClampedArray(new SharedArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.Shared), Inspect("new Int8Array(new SharedArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.Shared), Inspect("new Uint16Array(new SharedArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.Shared), Inspect("new Int16Array(new SharedArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.Shared), Inspect("new Uint32Array(new SharedArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.Shared), Inspect("new Int32Array(new SharedArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.Shared), Inspect("new BigUint64Array(new SharedArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.Shared), Inspect("new BigInt64Array(new SharedArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.Shared), Inspect("new Float32Array(new SharedArrayBuffer(256))"));
            Assert.AreEqual((JavaScriptObjectKind.TypedArray, JavaScriptObjectFlags.Shared), Inspect("new Float64Array(new SharedArrayBuffer(256))"));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ConstructorBinding()
        {
            engine.AddHostType("Foo", typeof(ConstructorBindingTest));
            dynamic test = engine.Evaluate(@"(function(length) {
                const a = new Array(length);
                for (let i = 0; i < length; ++i)
                    a[i] = new Foo(123);
                const b = new Array(length);
                for (let i = 0; i < length; ++i)
                    b[i] = new Foo('qux');
                const c = new Array(length);
                for (let i = 0; i < length; ++i)
                    c[i] = new Foo(456.789);
                return { a, b, c };
            })");

            const int length = 1000;
            var result = (IDictionary<string, object>)test(length);

            var a = (IList<object>)result["a"];
            var b = (IList<object>)result["b"];
            var c = (IList<object>)result["c"];

            Assert.AreEqual(length, a.Count);
            Assert.AreEqual(length, b.Count);
            Assert.AreEqual(length, c.Count);

            Assert.IsTrue(a.All(value => ((ConstructorBindingTest)value).A == 123));
            Assert.IsTrue(b.All(value => ((ConstructorBindingTest)value).A == 456));
            Assert.IsTrue(c.All(value => ((ConstructorBindingTest)value).A == 789));
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Compilation_CacheResult()
        {
            using (var runtime = new V8Runtime())
            {
                engine.Dispose();
                engine = runtime.CreateScriptEngine(); // default engine enables debugging, which disables caching (in older V8 versions)

                const string code = "obj = { foo: 123, bar: 'baz', qux: 456.789 }; count = 0; for (let name in obj) count += 1; Math.PI";
                var info = new DocumentInfo("foo.js");
                byte[] goodCacheBytes;

                {
                    byte[] cacheBytes = null;
                    var script = engine.Compile(code, V8CacheKind.None, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Disabled, cacheResult);
                    Assert.IsNull(cacheBytes);
                }

                {
                    byte[] cacheBytes = null;
                    var script = engine.Compile(info, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                    goodCacheBytes = cacheBytes;
                }

                {
                    var cacheBytes = ArrayHelpers.GetEmptyArray<byte>();
                    var script = engine.Compile(code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes;
                    var script = engine.Compile(code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Accepted, cacheResult);
                    Assert.AreEqual(goodCacheBytes, cacheBytes);
                }

                {
                    var cacheBytes = goodCacheBytes.ToArray();
                    var script = engine.Compile(code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Accepted, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes.ToArray();
                    var script = engine.Compile(info, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Verified, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes.Take(goodCacheBytes.Length - 1).ToArray();
                    var script = engine.Compile(code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    byte[] cacheBytes = null;
                    var script = runtime.Compile(code, V8CacheKind.None, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Disabled, cacheResult);
                    Assert.IsNull(cacheBytes);
                }

                {
                    byte[] cacheBytes = null;
                    var script = runtime.Compile(code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = ArrayHelpers.GetEmptyArray<byte>();
                    var script = runtime.Compile(code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes;
                    var script = runtime.Compile(code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Accepted, cacheResult);
                    Assert.AreEqual(goodCacheBytes, cacheBytes);
                }

                {
                    var cacheBytes = goodCacheBytes.ToArray();
                    var script = runtime.Compile(code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Accepted, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes.ToArray();
                    var script = runtime.Compile(info, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Verified, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes.Take(goodCacheBytes.Length - 1).ToArray();
                    var script = runtime.Compile(code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_Compilation_CacheResult_Module()
        {
            using (var runtime = new V8Runtime())
            {
                engine.Dispose();
                engine = runtime.CreateScriptEngine(); // default engine enables debugging, which disables caching (in older V8 versions)

                const string code = "let obj = { foo: 123, bar: 'baz', qux: 456.789 }; let count = 0; for (let name in obj) count += 1; import.meta.setResult(Math.PI)";
                var info = new DocumentInfo("foo.js") { Category = ModuleCategory.Standard };
                byte[] goodCacheBytes;

                {
                    byte[] cacheBytes = null;
                    var script = engine.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.None, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Disabled, cacheResult);
                    Assert.IsNull(cacheBytes);
                }

                {
                    byte[] cacheBytes = null;
                    var script = engine.Compile(info, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                    goodCacheBytes = cacheBytes;
                }

                {
                    var cacheBytes = ArrayHelpers.GetEmptyArray<byte>();
                    var script = engine.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes;
                    var script = engine.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Accepted, cacheResult);
                    Assert.AreEqual(goodCacheBytes, cacheBytes);
                }

                {
                    var cacheBytes = goodCacheBytes.ToArray();
                    var script = engine.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Accepted, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes.ToArray();
                    var script = engine.Compile(info, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Verified, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes.Take(goodCacheBytes.Length - 1).ToArray();
                    var script = engine.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    byte[] cacheBytes = null;
                    var script = runtime.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.None, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Disabled, cacheResult);
                    Assert.IsNull(cacheBytes);
                }

                {
                    byte[] cacheBytes = null;
                    var script = runtime.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = ArrayHelpers.GetEmptyArray<byte>();
                    var script = runtime.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes;
                    var script = runtime.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Accepted, cacheResult);
                    Assert.AreEqual(goodCacheBytes, cacheBytes);
                }

                {
                    var cacheBytes = goodCacheBytes.ToArray();
                    var script = runtime.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Accepted, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes.ToArray();
                    var script = runtime.Compile(info, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Verified, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes.Take(goodCacheBytes.Length - 1).ToArray();
                    var script = runtime.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, code, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DocumentCompilation_CacheResult()
        {
            using (var runtime = new V8Runtime())
            {
                engine.Dispose();
                engine = runtime.CreateScriptEngine(); // default engine enables debugging, which disables caching (in older V8 versions)

                const string code = "obj = { foo: 123, bar: 'baz', qux: 456.789 }; count = 0; for (let name in obj) count += 1; Math.PI";
                byte[] goodCacheBytes;

                runtime.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
                runtime.DocumentSettings.AddSystemDocument("foo.js", code);
                engine.DocumentSettings = runtime.DocumentSettings;

                {
                    byte[] cacheBytes = null;
                    var script = engine.CompileDocument("foo.js", V8CacheKind.None, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Disabled, cacheResult);
                    Assert.IsNull(cacheBytes);
                }

                {
                    byte[] cacheBytes = null;
                    var script = engine.CompileDocument("foo.js", V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                    goodCacheBytes = cacheBytes;
                }

                {
                    var cacheBytes = ArrayHelpers.GetEmptyArray<byte>();
                    var script = engine.CompileDocument("foo.js", V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes;
                    var script = engine.CompileDocument("foo.js", V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Verified, cacheResult);
                    Assert.AreEqual(goodCacheBytes, cacheBytes);
                }

                {
                    var cacheBytes = goodCacheBytes.ToArray();
                    var script = engine.CompileDocument("foo.js", V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Verified, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes.Take(goodCacheBytes.Length - 1).ToArray();
                    var script = engine.CompileDocument("foo.js", V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    byte[] cacheBytes = null;
                    var script = runtime.CompileDocument("foo.js", V8CacheKind.None, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Disabled, cacheResult);
                    Assert.IsNull(cacheBytes);
                }

                {
                    byte[] cacheBytes = null;
                    var script = runtime.CompileDocument("foo.js", V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = ArrayHelpers.GetEmptyArray<byte>();
                    var script = runtime.CompileDocument("foo.js", V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes;
                    var script = runtime.CompileDocument("foo.js", V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Verified, cacheResult);
                    Assert.AreEqual(goodCacheBytes, cacheBytes);
                }

                {
                    var cacheBytes = goodCacheBytes.ToArray();
                    var script = runtime.CompileDocument("foo.js", V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Verified, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes.Take(goodCacheBytes.Length - 1).ToArray();
                    var script = runtime.CompileDocument("foo.js", V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_DocumentCompilation_CacheResult_Module()
        {
            using (var runtime = new V8Runtime())
            {
                engine.Dispose();
                engine = runtime.CreateScriptEngine(); // default engine enables debugging, which disables caching (in older V8 versions)

                const string code = "let obj = { foo: 123, bar: 'baz', qux: 456.789 }; let count = 0; for (let name in obj) count += 1; import.meta.setResult(Math.PI)";
                byte[] goodCacheBytes;

                runtime.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
                runtime.DocumentSettings.AddSystemDocument("foo.js", ModuleCategory.Standard, code);
                engine.DocumentSettings = runtime.DocumentSettings;

                {
                    byte[] cacheBytes = null;
                    var script = engine.CompileDocument("foo.js", ModuleCategory.Standard, V8CacheKind.None, ref cacheBytes, out var cacheResult);
                    Assert.AreEqual(Math.PI, engine.Evaluate(script));
                    Assert.AreEqual(V8CacheResult.Disabled, cacheResult);
                    Assert.IsNull(cacheBytes);
                }

                {
                    byte[] cacheBytes = null;
                    var script = engine.CompileDocument("foo.js", ModuleCategory.Standard, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                    goodCacheBytes = cacheBytes;
                }

                {
                    var cacheBytes = ArrayHelpers.GetEmptyArray<byte>();
                    var script = engine.CompileDocument("foo.js", ModuleCategory.Standard, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes;
                    var script = engine.CompileDocument("foo.js", ModuleCategory.Standard, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Verified, cacheResult);
                    Assert.AreEqual(goodCacheBytes, cacheBytes);
                }

                {
                    var cacheBytes = goodCacheBytes.ToArray();
                    var script = engine.CompileDocument("foo.js", ModuleCategory.Standard, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Verified, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes.Take(goodCacheBytes.Length - 1).ToArray();
                    var script = engine.CompileDocument("foo.js", ModuleCategory.Standard, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    byte[] cacheBytes = null;
                    var script = runtime.CompileDocument("foo.js", ModuleCategory.Standard, V8CacheKind.None, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Disabled, cacheResult);
                    Assert.IsNull(cacheBytes);
                }

                {
                    byte[] cacheBytes = null;
                    var script = runtime.CompileDocument("foo.js", ModuleCategory.Standard, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = ArrayHelpers.GetEmptyArray<byte>();
                    var script = runtime.CompileDocument("foo.js", ModuleCategory.Standard, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes;
                    var script = runtime.CompileDocument("foo.js", ModuleCategory.Standard, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Verified, cacheResult);
                    Assert.AreEqual(goodCacheBytes, cacheBytes);
                }

                {
                    var cacheBytes = goodCacheBytes.ToArray();
                    var script = runtime.CompileDocument("foo.js", ModuleCategory.Standard, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Verified, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }

                {
                    var cacheBytes = goodCacheBytes.Take(goodCacheBytes.Length - 1).ToArray();
                    var script = runtime.CompileDocument("foo.js", ModuleCategory.Standard, V8CacheKind.Code, ref cacheBytes, out var cacheResult);
                    Assert.IsInstanceOfType(engine.Evaluate(script), typeof(Undefined));
                    Assert.AreEqual(V8CacheResult.Updated, cacheResult);
                    Assert.IsNotNull(cacheBytes);
                    Assert.IsTrue(cacheBytes.Length > 0);
                }
            }
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_UseSynchronizationContexts()
        {
            Func<SingleThreadSynchronizationContext, Task<bool>> doWork = context => Task<bool>.Factory.StartNew(() =>
            {
                Thread.Sleep(100);
                return context.OnThread;
            });

            var managedResults = SingleThreadSynchronizationContext.RunTask(async context =>
            {
                var results = new List<bool> { context.OnThread };
                results.Add(await doWork(context));
                results.Add(context.OnThread);
                return results;
            });

            Assert.IsTrue(managedResults.SequenceEqual(new[] { true, false, true }));

            // ReSharper disable AccessToDisposedClosure

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableTaskPromiseConversion);
            engine.Script.doWork = doWork;
            var scriptResults = (IList<object>)SingleThreadSynchronizationContext.RunTask(async context =>
            {
                engine.Script.context = context;
                return await (Task<object>)engine.Evaluate(@"(async function () {
                    const results = [context.OnThread];
                    results.push(await doWork(context));
                    results.push(context.OnThread);
                    return results;
                })()");
            });

            Assert.IsTrue(scriptResults.SequenceEqual(new object[] { true, false, false }));

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableTaskPromiseConversion | V8ScriptEngineFlags.UseSynchronizationContexts);
            engine.Script.doWork = doWork;
            scriptResults = (IList<object>)SingleThreadSynchronizationContext.RunTask(async context =>
            {
                engine.Script.context = context;
                return await (Task<object>)engine.Evaluate(@"(async function () {
                    const results = [context.OnThread];
                    results.push(await doWork(context));
                    results.push(context.OnThread);
                    return results;
                })()");
            });

            Assert.IsTrue(scriptResults.SequenceEqual(new object[] { true, false, true }));

            // ReSharper restore AccessToDisposedClosure
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_PerformanceObject()
        {
            Assert.IsInstanceOfType(engine.Script.Performance, typeof(Undefined));

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.AddPerformanceObject);

            Assert.IsInstanceOfType(engine.Script.Performance, typeof(IJavaScriptObject));
            Assert.IsInstanceOfType(engine.Script.Performance.sleep, typeof(IJavaScriptObject));
            Assert.IsInstanceOfType(engine.Script.Performance.now, typeof(IJavaScriptObject));

            var elapsed = Convert.ToDouble(engine.Evaluate(@"(() => {
                const start = Performance.now();
                Performance.sleep(25);
                return Performance.now() - start;
            })()"));

            Assert.IsTrue(elapsed >= 25);

            elapsed = Convert.ToDouble(engine.Evaluate(@"(() => {
                const start = Performance.now();
                Performance.sleep(25, false);
                return Performance.now() - start;
            })()"));

            Assert.IsTrue(elapsed >= 25);

            var average = Convert.ToDouble(engine.Evaluate(@"(() => {
                const start = Performance.now();
                for (let i = 0; i < 1000; ++i) Performance.sleep(5, true);
                return (Performance.now() - start) / 1000;
            })()"));

            Assert.IsTrue((average >= 5) && (average < 7.5));

            var delta = Convert.ToDouble(engine.Evaluate(@"
                Math.abs(Performance.timeOrigin + Performance.now() - Date.now());
            "));

            Assert.IsTrue(delta < 5);
        }

        [TestMethod, TestCategory("V8ScriptEngine")]
        public void V8ScriptEngine_ArrayConversion()
        {
            engine.Script.array = new object[0];
            var array = engine.Script.array as object[];
            Assert.IsNotNull(array);

            var scriptArray = engine.Evaluate("([])") as IJavaScriptObject;
            Assert.IsNotNull(scriptArray);
            Assert.IsTrue(scriptArray.Kind == JavaScriptObjectKind.Array);

            engine.Dispose();
            engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.EnableArrayConversion | V8ScriptEngineFlags.EnableDateTimeConversion);

            engine.Execute(@"
                array = [ 123, 'foo' ];
                array.push(new Date(), [ 456, 'bar', array ]);
            ");

            array = engine.Script.array as object[];
            Assert.IsNotNull(array);
            Assert.AreEqual(4, array.Length);
            Assert.AreEqual(123, array[0]);
            Assert.AreEqual("foo", array[1]);
            Assert.IsInstanceOfType(array[2], typeof(DateTime));

            var innerArray = array[3] as object[];
            Assert.IsNotNull(innerArray);
            Assert.AreEqual(3, innerArray.Length);
            Assert.AreEqual(456, innerArray[0]);
            Assert.AreEqual("bar", innerArray[1]);
            Assert.AreEqual(array, innerArray[2]);

            array = new object[] { 789, "baz", DateTime.Now, null };
            array[3] = new object[] { 987, "qux", array };
            engine.Script.array = array;

            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("Array.isArray(array)")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("array.length === 4")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("array[0] === 789")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("array[1] === 'baz'")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("array[2] instanceof Date")));

            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("Array.isArray(array[3])")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("array[3].length === 3")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("array[3][0] === 987")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("array[3][1] === 'qux'")));
            Assert.IsTrue(Convert.ToBoolean(engine.Evaluate("array[3][2] === array")));
        }

        // ReSharper restore InconsistentNaming

        #endregion

        #region miscellaneous

        private const string generalScript =
        @"
            System = clr.System;

            TestObject = host.type('Microsoft.ClearScript.Test.GeneralTestObject', 'ClearScriptTest');
            tlist = host.newObj(System.Collections.Generic.List(TestObject));
            tlist.Add(host.newObj(TestObject, 'Eóin', 20));
            tlist.Add(host.newObj(TestObject, 'Shane', 16));
            tlist.Add(host.newObj(TestObject, 'Cillian', 8));
            tlist.Add(host.newObj(TestObject, 'Sasha', 6));
            tlist.Add(host.newObj(TestObject, 'Brian', 3));

            olist = host.newObj(System.Collections.Generic.List(System.Object));
            olist.Add({ name: 'Brian', age: 3 });
            olist.Add({ name: 'Sasha', age: 6 });
            olist.Add({ name: 'Cillian', age: 8 });
            olist.Add({ name: 'Shane', age: 16 });
            olist.Add({ name: 'Eóin', age: 20 });

            dict = host.newObj(System.Collections.Generic.Dictionary(System.String, System.String));
            dict.Add('foo', 'bar');
            dict.Add('baz', 'qux');
            value = host.newVar(System.String);
            result = dict.TryGetValue('foo', value.out);

            bag = host.newObj();
            bag.method = function (x) { System.Console.WriteLine(x * x); };
            bag.proc = host.del(System.Action(System.Object), bag.method);

            expando = host.newObj(System.Dynamic.ExpandoObject);
            expandoCollection = host.cast(System.Collections.Generic.ICollection(System.Collections.Generic.KeyValuePair(System.String, System.Object)), expando);

            function onChange(s, e) {
                System.Console.WriteLine('Property changed: {0}; new value: {1}', e.PropertyName, s[e.PropertyName]);
            };
            function onStaticChange(s, e) {
                System.Console.WriteLine('Property changed: {0}; new value: {1} (static event)', e.PropertyName, e.PropertyValue);
            };
            eventCookie = tlist.Item(0).Change.connect(onChange);
            staticEventCookie = TestObject.StaticChange.connect(onStaticChange);
            tlist.Item(0).Name = 'Jerry';
            tlist.Item(1).Name = 'Ellis';
            tlist.Item(0).Name = 'Eóin';
            tlist.Item(1).Name = 'Shane';
            eventCookie.disconnect();
            staticEventCookie.disconnect();
            tlist.Item(0).Name = 'Jerry';
            tlist.Item(1).Name = 'Ellis';
            tlist.Item(0).Name = 'Eóin';
            tlist.Item(1).Name = 'Shane';
        ";

        private const string generalScriptOutput =
        @"
            Property changed: Name; new value: Jerry
            Property changed: Name; new value: Jerry (static event)
            Property changed: Name; new value: Ellis (static event)
            Property changed: Name; new value: Eóin
            Property changed: Name; new value: Eóin (static event)
            Property changed: Name; new value: Shane (static event)
        ";

        private static string CreateCpuProfileTestScript()
        {
            var builder = new StringBuilder();

            builder.Append(@"
                function loop() {
                    for (var i = 0; i < 10000000; i++) {
                        for (var j = 0; j < 10000000; j++) {
                            if (Math.random() > 0.999 && Math.random() > 0.999) {
                                return i + '-' + j;
                            }
                        }
                    }
                }
                (function () {");

            builder.AppendLine();
            AppendCpuProfileTestSequence(builder, 4, MiscHelpers.CreateSeededRandom(), new List<int>());
            builder.Append("                })()");
            builder.AppendLine();

            return builder.ToString();
        }

        private static void AppendCpuProfileTestSequence(StringBuilder builder, int count, Random random, List<int> indices)
        {
            const string separator = "_";
            var indent = new string(Enumerable.Repeat(' ', indices.Count * 4 + 20).ToArray());

            count = (count < 0) ? random.Next(4) : count;
            count = (indices.Count >= 4) ? 0 : count;

            for (var index = 0; index < count; index++)
            {
                builder.AppendFormat("{0}function f{1}{2}() {{", indent, separator, string.Join(separator, indices.Concat(index.ToEnumerable())));
                builder.AppendLine();

                AppendCpuProfileTestSequence(builder, -1, random, indices.Concat(index.ToEnumerable()).ToList());

                builder.AppendFormat("{0}}}", indent);
                builder.AppendLine();
            }

            builder.AppendFormat("{0}return {1}loop();", indent, string.Join(string.Empty, Enumerable.Range(0, count).Select(index => "f" + separator + string.Join(separator, indices.Concat(index.ToEnumerable())) + "() + '-' + ")));
            builder.AppendLine();
        }

        public object TestProperty { get; set; }

        public static object StaticTestProperty { get; set; }

        private void TestBigInt(BigInteger value)
        {
            engine.Script.value = value;
            Assert.AreEqual("bigint", engine.Evaluate("typeof value"));
            Assert.IsInstanceOfType(engine.Script.value, typeof(BigInteger));
            Assert.AreEqual(value, engine.Script.value);
            Assert.IsInstanceOfType(engine.Evaluate("value"), typeof(BigInteger));
            Assert.AreEqual(value, engine.Evaluate("value"));
            Assert.AreEqual(value.ToString(), engine.Evaluate("value.toString()"));
        }

        private void TestCamelCaseMemberBinding()
        {
            var random = MiscHelpers.CreateSeededRandom();
            var makeIntArray = new Func<int[]>(() => Enumerable.Range(0, random.Next(5, 25)).Select(_ => random.Next(int.MinValue, int.MaxValue)).ToArray());
            var makeShort = new Func<short>(() => Convert.ToInt16(random.Next(short.MinValue, short.MaxValue)));
            var makeEnum = new Func<TestEnum>(() => (TestEnum)random.Next(0, 5));
            var makeTimeSpan = new Func<TimeSpan>(() => TimeSpan.FromMilliseconds(makeShort()));

            var testObject = new TestObject
            {
                BaseField = makeIntArray(),
                BaseScalarField = makeShort(),
                BaseEnumField = makeEnum(),
                BaseStructField = makeTimeSpan(),

                BaseProperty = makeIntArray(),
                BaseScalarProperty = makeShort(),
                BaseEnumProperty = makeEnum(),
                BaseStructProperty = makeTimeSpan(),

                BaseInterfaceProperty = makeIntArray(),
                BaseInterfaceScalarProperty = makeShort(),
                BaseInterfaceEnumProperty = makeEnum(),
                BaseInterfaceStructProperty = makeTimeSpan(),

                Field = new[] { 0, 9, 1, 8, 2, 7, 3, 6, 4, 5 },
                ScalarField = makeShort(),
                EnumField = makeEnum(),
                StructField = makeTimeSpan(),

                Property = makeIntArray(),
                ScalarProperty = makeShort(),
                EnumProperty = makeEnum(),
                StructProperty = makeTimeSpan(),

                InterfaceProperty = makeIntArray(),
                InterfaceScalarProperty = makeShort(),
                InterfaceEnumProperty = makeEnum(),
                InterfaceStructProperty = makeTimeSpan(),
            };

            var explicitBaseTestInterface = (IExplicitBaseTestInterface)testObject;
            explicitBaseTestInterface.ExplicitBaseInterfaceProperty = makeIntArray();
            explicitBaseTestInterface.ExplicitBaseInterfaceScalarProperty = makeShort();
            explicitBaseTestInterface.ExplicitBaseInterfaceEnumProperty = makeEnum();
            explicitBaseTestInterface.ExplicitBaseInterfaceStructProperty = makeTimeSpan();

            var explicitTestInterface = (IExplicitTestInterface)testObject;
            explicitTestInterface.ExplicitInterfaceProperty = makeIntArray();
            explicitTestInterface.ExplicitInterfaceScalarProperty = makeShort();
            explicitTestInterface.ExplicitInterfaceEnumProperty = makeEnum();
            explicitTestInterface.ExplicitInterfaceStructProperty = makeTimeSpan();

            engine.AddHostType(typeof(TestEnum));
            engine.Script.testObject = testObject;
            engine.Script.testBaseInterface = testObject.ToRestrictedHostObject<IBaseTestInterface>(engine);
            engine.Script.testInterface = testObject.ToRestrictedHostObject<ITestInterface>(engine);
            engine.Script.explicitBaseTestInterface = testObject.ToRestrictedHostObject<IExplicitBaseTestInterface>(engine);
            engine.Script.explicitTestInterface = testObject.ToRestrictedHostObject<IExplicitTestInterface>(engine);

            Assert.IsTrue(testObject.BaseField.SequenceEqual((int[])engine.Evaluate("testObject.baseField")));
            Assert.AreEqual(testObject.BaseScalarField, Convert.ToInt16(engine.Evaluate("testObject.baseScalarField")));
            Assert.AreEqual(testObject.BaseEnumField, engine.Evaluate("testObject.baseEnumField"));
            Assert.AreEqual(testObject.BaseStructField, engine.Evaluate("testObject.baseStructField"));

            Assert.IsTrue(testObject.BaseProperty.SequenceEqual((int[])engine.Evaluate("testObject.baseProperty")));
            Assert.AreEqual(testObject.BaseScalarProperty, Convert.ToInt16(engine.Evaluate("testObject.baseScalarProperty")));
            Assert.AreEqual(testObject.BaseEnumProperty, engine.Evaluate("testObject.baseEnumProperty"));
            Assert.AreEqual(testObject.BaseStructProperty, engine.Evaluate("testObject.baseStructProperty"));
            Assert.AreEqual(testObject.BaseReadOnlyProperty, Convert.ToByte(engine.Evaluate("testObject.baseReadOnlyProperty")));

            engine.Execute("var connection = testObject.baseEvent.connect((sender, args) => sender.baseScalarProperty = args.arg);");
            var arg = makeShort();
            testObject.BaseFireEvent(arg);
            Assert.AreEqual(arg, testObject.BaseScalarProperty);
            engine.Execute("connection.disconnect();");
            testObject.BaseFireEvent(makeShort());
            Assert.AreEqual(arg, testObject.BaseScalarProperty);

            Assert.AreEqual(testObject.BaseMethod("foo", 4), engine.Evaluate("testObject.baseMethod('foo', 4)"));
            Assert.AreEqual(testObject.BaseMethod("foo", 4, TestEnum.Second), engine.Evaluate("testObject.baseMethod('foo', 4, TestEnum.second)"));
            Assert.AreEqual(testObject.BaseMethod<TestEnum>(4), engine.Evaluate("testObject.baseMethod(TestEnum, 4)"));
            Assert.AreEqual(testObject.BaseBindTestMethod(Math.PI), engine.Evaluate("testObject.baseBindTestMethod(Math.PI)"));

            Assert.IsTrue(testObject.BaseInterfaceProperty.SequenceEqual((int[])engine.Evaluate("testObject.baseInterfaceProperty")));
            Assert.AreEqual(testObject.BaseInterfaceScalarProperty, Convert.ToInt16(engine.Evaluate("testObject.baseInterfaceScalarProperty")));
            Assert.AreEqual(testObject.BaseInterfaceEnumProperty, engine.Evaluate("testObject.baseInterfaceEnumProperty"));
            Assert.AreEqual(testObject.BaseInterfaceStructProperty, engine.Evaluate("testObject.baseInterfaceStructProperty"));
            Assert.AreEqual(testObject.BaseInterfaceReadOnlyProperty, Convert.ToByte(engine.Evaluate("testObject.baseInterfaceReadOnlyProperty")));

            engine.Execute("var connection = testObject.baseInterfaceEvent.connect((sender, args) => sender.baseInterfaceScalarProperty = args.arg);");
            arg = makeShort();
            testObject.BaseInterfaceFireEvent(arg);
            Assert.AreEqual(arg, testObject.BaseInterfaceScalarProperty);
            engine.Execute("connection.disconnect();");
            testObject.BaseInterfaceFireEvent(makeShort());
            Assert.AreEqual(arg, testObject.BaseInterfaceScalarProperty);

            Assert.AreEqual(testObject.BaseInterfaceMethod("foo", 4), engine.Evaluate("testObject.baseInterfaceMethod('foo', 4)"));
            Assert.AreEqual(testObject.BaseInterfaceMethod("foo", 4, TestEnum.Second), engine.Evaluate("testObject.baseInterfaceMethod('foo', 4, TestEnum.second)"));
            Assert.AreEqual(testObject.BaseInterfaceMethod<TestEnum>(4), engine.Evaluate("testObject.baseInterfaceMethod(TestEnum, 4)"));
            Assert.AreEqual(testObject.BaseInterfaceBindTestMethod(Math.PI), engine.Evaluate("testObject.baseInterfaceBindTestMethod(Math.PI)"));

            Assert.IsTrue(testObject.Field.SequenceEqual((int[])engine.Evaluate("testObject.field")));
            Assert.AreEqual(testObject.ScalarField, Convert.ToInt16(engine.Evaluate("testObject.scalarField")));
            Assert.AreEqual(testObject.EnumField, engine.Evaluate("testObject.enumField"));
            Assert.AreEqual(testObject.StructField, engine.Evaluate("testObject.structField"));

            Assert.IsTrue(testObject.Property.SequenceEqual((int[])engine.Evaluate("testObject.property")));
            Assert.AreEqual(testObject.ScalarProperty, Convert.ToInt16(engine.Evaluate("testObject.scalarProperty")));
            Assert.AreEqual(testObject.EnumProperty, engine.Evaluate("testObject.enumProperty"));
            Assert.AreEqual(testObject.StructProperty, engine.Evaluate("testObject.structProperty"));
            Assert.AreEqual(testObject.ReadOnlyProperty, Convert.ToByte(engine.Evaluate("testObject.readOnlyProperty")));

            engine.Execute("var connection = testObject.event.connect((sender, args) => sender.scalarProperty = args.arg);");
            arg = makeShort();
            testObject.FireEvent(arg);
            Assert.AreEqual(arg, testObject.ScalarProperty);
            engine.Execute("connection.disconnect();");
            testObject.FireEvent(makeShort());
            Assert.AreEqual(arg, testObject.ScalarProperty);

            Assert.AreEqual(testObject.Method("foo", 4), engine.Evaluate("testObject.method('foo', 4)"));
            Assert.AreEqual(testObject.Method("foo", 4, TestEnum.Second), engine.Evaluate("testObject.method('foo', 4, TestEnum.second)"));
            Assert.AreEqual(testObject.Method<TestEnum>(4), engine.Evaluate("testObject.method(TestEnum, 4)"));
            Assert.AreEqual(testObject.BindTestMethod(Math.PI), engine.Evaluate("testObject.bindTestMethod(Math.PI)"));

            Assert.IsTrue(testObject.InterfaceProperty.SequenceEqual((int[])engine.Evaluate("testObject.interfaceProperty")));
            Assert.AreEqual(testObject.InterfaceScalarProperty, Convert.ToInt16(engine.Evaluate("testObject.interfaceScalarProperty")));
            Assert.AreEqual(testObject.InterfaceEnumProperty, engine.Evaluate("testObject.interfaceEnumProperty"));
            Assert.AreEqual(testObject.InterfaceStructProperty, engine.Evaluate("testObject.interfaceStructProperty"));
            Assert.AreEqual(testObject.InterfaceReadOnlyProperty, Convert.ToByte(engine.Evaluate("testObject.interfaceReadOnlyProperty")));

            engine.Execute("var connection = testObject.interfaceEvent.connect((sender, args) => sender.interfaceScalarProperty = args.arg);");
            arg = makeShort();
            testObject.InterfaceFireEvent(arg);
            Assert.AreEqual(arg, testObject.InterfaceScalarProperty);
            engine.Execute("connection.disconnect();");
            testObject.InterfaceFireEvent(makeShort());
            Assert.AreEqual(arg, testObject.InterfaceScalarProperty);

            Assert.AreEqual(testObject.InterfaceMethod("foo", 4), engine.Evaluate("testObject.interfaceMethod('foo', 4)"));
            Assert.AreEqual(testObject.InterfaceMethod("foo", 4, TestEnum.Second), engine.Evaluate("testObject.interfaceMethod('foo', 4, TestEnum.second)"));
            Assert.AreEqual(testObject.InterfaceMethod<TestEnum>(4), engine.Evaluate("testObject.interfaceMethod(TestEnum, 4)"));
            Assert.AreEqual(testObject.InterfaceBindTestMethod(Math.PI), engine.Evaluate("testObject.interfaceBindTestMethod(Math.PI)"));

            Assert.IsTrue(explicitBaseTestInterface.ExplicitBaseInterfaceProperty.SequenceEqual((int[])engine.Evaluate("explicitBaseTestInterface.explicitBaseInterfaceProperty")));
            Assert.AreEqual(explicitBaseTestInterface.ExplicitBaseInterfaceScalarProperty, Convert.ToInt16(engine.Evaluate("explicitBaseTestInterface.explicitBaseInterfaceScalarProperty")));
            Assert.AreEqual(explicitBaseTestInterface.ExplicitBaseInterfaceEnumProperty, engine.Evaluate("explicitBaseTestInterface.explicitBaseInterfaceEnumProperty"));
            Assert.AreEqual(explicitBaseTestInterface.ExplicitBaseInterfaceStructProperty, engine.Evaluate("explicitBaseTestInterface.explicitBaseInterfaceStructProperty"));
            Assert.AreEqual(explicitBaseTestInterface.ExplicitBaseInterfaceReadOnlyProperty, Convert.ToByte(engine.Evaluate("explicitBaseTestInterface.explicitBaseInterfaceReadOnlyProperty")));

            engine.Execute("var connection = explicitBaseTestInterface.explicitBaseInterfaceEvent.connect((sender, args) => explicitBaseTestInterface.explicitBaseInterfaceScalarProperty = args.arg);");
            arg = makeShort();
            explicitBaseTestInterface.ExplicitBaseInterfaceFireEvent(arg);
            Assert.AreEqual(arg, explicitBaseTestInterface.ExplicitBaseInterfaceScalarProperty);
            engine.Execute("connection.disconnect();");
            explicitBaseTestInterface.ExplicitBaseInterfaceFireEvent(makeShort());
            Assert.AreEqual(arg, explicitBaseTestInterface.ExplicitBaseInterfaceScalarProperty);

            Assert.AreEqual(explicitBaseTestInterface.ExplicitBaseInterfaceMethod("foo", 4), engine.Evaluate("explicitBaseTestInterface.explicitBaseInterfaceMethod('foo', 4)"));
            Assert.AreEqual(explicitBaseTestInterface.ExplicitBaseInterfaceMethod("foo", 4, TestEnum.Second), engine.Evaluate("explicitBaseTestInterface.explicitBaseInterfaceMethod('foo', 4, TestEnum.second)"));
            Assert.AreEqual(explicitBaseTestInterface.ExplicitBaseInterfaceMethod<TestEnum>(4), engine.Evaluate("explicitBaseTestInterface.explicitBaseInterfaceMethod(TestEnum, 4)"));
            Assert.AreEqual(explicitBaseTestInterface.ExplicitBaseInterfaceBindTestMethod(Math.PI), engine.Evaluate("explicitBaseTestInterface.explicitBaseInterfaceBindTestMethod(Math.PI)"));

            Assert.IsTrue(explicitTestInterface.ExplicitInterfaceProperty.SequenceEqual((int[])engine.Evaluate("explicitTestInterface.explicitInterfaceProperty")));
            Assert.AreEqual(explicitTestInterface.ExplicitInterfaceScalarProperty, Convert.ToInt16(engine.Evaluate("explicitTestInterface.explicitInterfaceScalarProperty")));
            Assert.AreEqual(explicitTestInterface.ExplicitInterfaceEnumProperty, engine.Evaluate("explicitTestInterface.explicitInterfaceEnumProperty"));
            Assert.AreEqual(explicitTestInterface.ExplicitInterfaceStructProperty, engine.Evaluate("explicitTestInterface.explicitInterfaceStructProperty"));
            Assert.AreEqual(explicitTestInterface.ExplicitInterfaceReadOnlyProperty, Convert.ToByte(engine.Evaluate("explicitTestInterface.explicitInterfaceReadOnlyProperty")));

            engine.Execute("var connection = explicitTestInterface.explicitInterfaceEvent.connect((sender, args) => explicitTestInterface.explicitInterfaceScalarProperty = args.arg);");
            arg = makeShort();
            explicitTestInterface.ExplicitInterfaceFireEvent(arg);
            Assert.AreEqual(arg, explicitTestInterface.ExplicitInterfaceScalarProperty);
            engine.Execute("connection.disconnect();");
            explicitTestInterface.ExplicitInterfaceFireEvent(makeShort());
            Assert.AreEqual(arg, explicitTestInterface.ExplicitInterfaceScalarProperty);

            Assert.AreEqual(explicitTestInterface.ExplicitInterfaceMethod("foo", 4), engine.Evaluate("explicitTestInterface.explicitInterfaceMethod('foo', 4)"));
            Assert.AreEqual(explicitTestInterface.ExplicitInterfaceMethod("foo", 4, TestEnum.Second), engine.Evaluate("explicitTestInterface.explicitInterfaceMethod('foo', 4, TestEnum.second)"));
            Assert.AreEqual(explicitTestInterface.ExplicitInterfaceMethod<TestEnum>(4), engine.Evaluate("explicitTestInterface.explicitInterfaceMethod(TestEnum, 4)"));
            Assert.AreEqual(explicitTestInterface.ExplicitInterfaceBindTestMethod(Math.PI), engine.Evaluate("explicitTestInterface.explicitInterfaceBindTestMethod(Math.PI)"));
        }

        private sealed class CamelCaseAttributeLoader : CustomAttributeLoader
        {
            public override T[] LoadCustomAttributes<T>(ICustomAttributeProvider resource, bool inherit)
            {
                if (typeof(T) == typeof(ScriptMemberAttribute) && (resource is MemberInfo member))
                {
                    var name = char.ToLowerInvariant(member.Name[0]) + member.Name.Substring(1);
                    return new[] { new ScriptMemberAttribute(name) } as T[];
                }

                return base.LoadCustomAttributes<T>(resource, inherit);
            }
        }

        // ReSharper disable UnusedMember.Local

        private void PrivateMethod()
        {
        }

        private static void PrivateStaticMethod()
        {
        }

        private delegate string TestDelegate(string pre, ref string value, int post);

        public delegate object VarArgDelegate(object pre, params object[] args);

        // ReSharper restore UnusedMember.Local

        public class ConstructorBindingTest
        {
            public int A;
            public string B;
            public double C;

            public ConstructorBindingTest(int a, string b = "foo", double c = 456.789)
            {
                A = a; B = b; C = c;
            }

            public ConstructorBindingTest(string b, int a = 456, double c = 789.987)
            {
                A = a; B = b; C = c;
            }

            public ConstructorBindingTest(double c, int a = 789, string b = "bar")
            {
                A = a; B = b; C = c;
            }
        }

        public class SingleThreadSynchronizationContext : SynchronizationContext
        {
            private readonly BlockingCollection<(SendOrPostCallback, object)> queue = new();
            private readonly Thread thread;

            private SingleThreadSynchronizationContext()
            {
                thread = new Thread(RunLoop);
                thread.Start();
            }

            public bool OnThread => Thread.CurrentThread == thread;

            public static T RunTask<T>(Func<SingleThreadSynchronizationContext, Task<T>> createTask)
            {
                var context = new SingleThreadSynchronizationContext();

                T result = default;
                var doneEvent = new ManualResetEventSlim();

                context.Post(_ => createTask(context).ContinueWith(task => { result = task.Result; doneEvent.Set(); }), null);
                doneEvent.Wait();

                context.queue.CompleteAdding();
                context.thread.Join();

                return result;
            }

            public override void Post(SendOrPostCallback callback, object state) => queue.Add((callback, state));

            private void RunLoop()
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            }
        }

        #endregion
    }
}
