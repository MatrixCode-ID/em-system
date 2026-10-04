using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Test.Models;

namespace Em.Test.Api
{
   public partial class TestServices
   {
      #region Business Task

      [PostAction]
      public Task<BusinessTaskInfo> PostGetMeta_TestStartTask(TestTaskRequest request) {
         // Everything the work needs is copied out of the request first: the work runs after this action
         // has returned, where the request, this service and its context no longer exist.
         var seconds = Math.Clamp(request.Seconds, 1, 120);
         var fail = request.Fail;
         var output = request.Output;

         var options = new BusinessTaskOptions {
            Key = $"{ITestServices.TaskKeyPrefix}{output}",
            Title = $"Test task: {output}, {seconds}s{(fail ? ", fails on purpose" : "")}",
            Scope = request.Global ? BusinessTaskScope.Global : BusinessTaskScope.Personal,
            NavigationName = "test.tasks",
            ResultFileName = output == TestTaskOutput.File ? "test-task-result.txt" : null,
            ResultContentType = output == TestTaskOutput.File ? "text/plain" : null
         };

         var info = output switch {
            TestTaskOutput.Json => StartBusinessTask(options, async tc => {
               await RunStepsAsync(tc, seconds, fail);
               return new { Seconds = seconds, FinishedAtUtc = DateTime.UtcNow, Starter = tc.Starter.cUserAccount };
            }),
            TestTaskOutput.File => StartBusinessTask(options, async (tc, file) => {
               await RunStepsAsync(tc, seconds, fail);
               await using var writer = new StreamWriter(file, leaveOpen: true);
               await writer.WriteLineAsync($"Em.Test business task finished after {seconds}s at {DateTime.UtcNow:O}.");
               await writer.WriteLineAsync($"Started by {tc.Starter.cUserAccount}.");
            }),
            _ => StartBusinessTask(options, tc => RunStepsAsync(tc, seconds, fail))
         };

         return Task.FromResult(info);
      }

      [GetAction]
      public Task<BusinessTaskInfo[]> GetMeta_TestGlobalTasks() =>
         Task.FromResult(FindBusinessTasks(ITestServices.TaskKeyPrefix, BusinessTaskScope.Global));

      [PostAction]
      public Task PostMeta_TestGlobalTaskCancel(string key) {
         EnsureTestTaskKey(key);
         CancelBusinessTask(key, BusinessTaskScope.Global);
         return Task.CompletedTask;
      }

      [PostAction]
      public Task PostMeta_TestGlobalTaskClear(string key) {
         EnsureTestTaskKey(key);
         ClearBusinessTask(key, BusinessTaskScope.Global);
         return Task.CompletedTask;
      }

      private static void EnsureTestTaskKey(string key) {
         // These actions are open to every holder of the module claim, so they must not reach the tasks
         // other modules started under their own keys.
         if (!key.StartsWith(ITestServices.TaskKeyPrefix, StringComparison.Ordinal)) {
            throw new ActionException("That is not a test task.", 400);
         }
      }

      private static async Task RunStepsAsync(BusinessTaskContext tc, int seconds, bool fail) {
         for (var step = 1; step <= seconds; step++) {
            await Task.Delay(TimeSpan.FromSeconds(1), tc.CancellationToken);
            tc.Report(100.0 * step / seconds, $"Step {step} of {seconds}");

            if (fail && step >= Math.Max(1, seconds / 2)) {
               throw new InvalidOperationException("The test task was set to fail halfway.");
            }
         }
      }

      #endregion
   }
}
