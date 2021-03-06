using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;


namespace Xunit.Runners.Visitors
{
    class TestExecutionVisitor : TestMessageVisitor<ITestAssemblyFinished>
    {
        readonly Func<bool> cancelledThunk;

        readonly SynchronizationContext context;
        readonly ITestFrameworkExecutionOptions executionOptions;
        readonly ITestListener listener;
        readonly Dictionary<ITestCase, TestCaseViewModel> testCases;

        public TestExecutionVisitor(Dictionary<ITestCase, TestCaseViewModel> testCases,
                                    ITestListener listener,
                                    ITestFrameworkExecutionOptions executionOptions,
                                    Func<bool> cancelledThunk,
                                    SynchronizationContext context)
        {
            if (testCases == null) throw new ArgumentNullException(nameof(testCases));
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            if (cancelledThunk == null) throw new ArgumentNullException(nameof(cancelledThunk));

            this.testCases = testCases;
            this.listener = listener;
            this.executionOptions = executionOptions;
            this.cancelledThunk = cancelledThunk;
            this.context = context;
        }

        protected override bool Visit(ITestFailed testFailed)
        {
            MakeTestResultViewModel(testFailed, TestState.Failed);
            return !cancelledThunk();
        }

        protected override bool Visit(ITestPassed testPassed)
        {
            MakeTestResultViewModel(testPassed, TestState.Passed);
            return !cancelledThunk();
        }

        protected override bool Visit(ITestSkipped testSkipped)
        {
            MakeTestResultViewModel(testSkipped, TestState.Skipped);
            return !cancelledThunk();
        }

        async void MakeTestResultViewModel(ITestResultMessage testResult, TestState outcome)
        {
            var tcs = new TaskCompletionSource<TestResultViewModel>();

            TestCaseViewModel testCase;
            if (!testCases.TryGetValue(testResult.TestCase, out testCase))
            {
                // no matching reference, search by Unique ID as a fallback
                testCase = testCases.FirstOrDefault(kvp => kvp.Key.UniqueID?.Equals(testResult.TestCase.UniqueID) ?? false).Value;
                if (testCase == null)
                    return;
            }

            // Create the result VM on the UI thread as it updates properties
            context.Post(_ =>
                         {
                   
                            var result = new TestResultViewModel(testCase, testResult)
                            {
                                Duration = TimeSpan.FromSeconds((double)testResult.ExecutionTime)
                            };


                            if (outcome == TestState.Failed)
                            {
                                result.ErrorMessage = ExceptionUtility.CombineMessages((ITestFailed)testResult);
                                result.ErrorStackTrace = ExceptionUtility.CombineStackTraces((ITestFailed)testResult);
                            }
   

                             tcs.TrySetResult(result);
                         }, null);

           
            var r = await tcs.Task;
     
            listener.RecordResult(r); // bring it back to the threadpool thread
        }
    }
}
