// Copyright (c) Joppe27 <joppe27.be>. Licensed under the MIT Licence.
// See LICENSE file in repository root for full license text.

#region

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.VisualTree;

#endregion

namespace SlskdMigrationTool.App;

public static class TaskUtilities
{
    /// <summary>
    /// Performs an asynchronous task while notifying the UI (to show loading indicator)
    /// </summary>
    public static async Task<TResult> AwaitNotify<TResult>(Visual sender, Task<TResult> awaitableTask)
    {
        MainWindow mainView = sender.GetVisualAncestors().SingleOrDefault(v => v is MainWindow) as MainWindow ??
                            throw new InvalidOperationException("Sender is not a child of a MainWindow");

        mainView.LoadingStarted.Invoke();

        TResult result;
        try
        {
            result = await awaitableTask;
        }
        finally
        {
            mainView.LoadingFinished.Invoke();
        }

        return result;
    }

    /// <summary>
    /// Performs an asynchronous task while notifying the UI (to show loading indicator)
    /// </summary>
    public static async Task AwaitNotify(Visual sender, Task awaitableTask)
    {
        MainWindow mainView = sender.GetVisualAncestors().SingleOrDefault(v => v is MainWindow) as MainWindow ??
                              throw new InvalidOperationException("Sender is not a child of a MainWindow");

        mainView.LoadingStarted.Invoke();

        try
        {
            await awaitableTask;
        }
        finally
        {
            mainView.LoadingFinished.Invoke();
        }
    }
}