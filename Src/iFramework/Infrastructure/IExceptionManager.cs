﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace IFramework.Infrastructure
{
    public interface IExceptionManager
    {
        Task<ApiResult<T>> ProcessAsync<T>(Func<Task<T>> func,
                                           bool needRetry = false,
                                           int retryCount = 50,
                                           bool continueOnCapturedContext = false,
                                           Func<Exception, string> getExceptionMessage = null);

        Task<ApiResult> ProcessAsync(Func<Task> func,
                                     bool needRetry = false,
                                     int retryCount = 50,
                                     bool continueOnCapturedContext = false,
                                     Func<Exception, string> getExceptionMessage = null);

        ApiResult Process(Action action,
                          bool needRetry = false,
                          int retryCount = 50,
                          Func<Exception, string> getExceptionMessage = null);

        ApiResult<T> Process<T>(Func<T> func,
                                bool needRetry = false,
                                int retryCount = 50,
                                Func<Exception, string> getExceptionMessage = null);
    }
}
