// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc.Core;
using Microsoft.AspNet.Mvc.Filters;
using Microsoft.AspNet.Mvc.Formatters;
using Microsoft.AspNet.Mvc.Internal;
using Microsoft.AspNet.Mvc.Logging;
using Microsoft.AspNet.Mvc.ModelBinding;
using Microsoft.AspNet.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNet.Mvc.Controllers
{
    public class ControllerActionInvoker : FilterActionInvoker
    {
        private readonly ControllerActionDescriptor _descriptor;
        private readonly IControllerFactory _controllerFactory;
        private readonly IControllerActionArgumentBinder _argumentBinder;

        public ControllerActionInvoker(
            ActionContext actionContext,
            FilterCache filterCache,
            IControllerFactory controllerFactory,
            ControllerActionDescriptor descriptor,
            IReadOnlyList<IInputFormatter> inputFormatters,
            IControllerActionArgumentBinder argumentBinder,
            IReadOnlyList<IModelBinder> modelBinders,
            IReadOnlyList<IModelValidatorProvider> modelValidatorProviders,
            IReadOnlyList<IValueProviderFactory> valueProviderFactories,
            ILogger logger,
            DiagnosticSource diagnosticSource,
            int maxModelValidationErrors)
            : base(
                  actionContext,
                  filterCache,
                  inputFormatters,
                  modelBinders,
                  modelValidatorProviders,
                  valueProviderFactories,
                  logger,
                  diagnosticSource,
                  maxModelValidationErrors)
        {
            if (controllerFactory == null)
            {
                throw new ArgumentNullException(nameof(controllerFactory));
            }

            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (argumentBinder == null)
            {
                throw new ArgumentNullException(nameof(argumentBinder));
            }

            _controllerFactory = controllerFactory;
            _descriptor = descriptor;
            _argumentBinder = argumentBinder;

            if (descriptor.MethodInfo == null)
            {
                throw new ArgumentException(
                    Resources.FormatPropertyOfTypeCannotBeNull(
                        nameof(descriptor.MethodInfo),
                        typeof(ControllerActionDescriptor)),
                    nameof(descriptor));
            }
        }

        protected override object CreateInstance()
        {
            return _controllerFactory.CreateController(Context);
        }

        protected override void ReleaseInstance(object instance)
        {
            _controllerFactory.ReleaseController(instance);
        }

        protected override async Task<IActionResult> InvokeActionAsync(ActionExecutingContext actionExecutingContext)
        {
            var actionMethodInfo = _descriptor.MethodInfo;
            var arguments = ControllerActionExecutor.PrepareArguments(
                actionExecutingContext.ActionArguments,
                actionMethodInfo.GetParameters());

            Logger.ActionMethodExecuting(actionExecutingContext, arguments);

            var actionReturnValue = await ControllerActionExecutor.ExecuteAsync(
                actionMethodInfo,
                actionExecutingContext.Controller,
                arguments);

            var actionResult = CreateActionResult(
                actionMethodInfo.ReturnType,
                actionReturnValue);

            Logger.ActionMethodExecuted(actionExecutingContext, actionResult);

            return actionResult;
        }

        protected override Task<IDictionary<string, object>> BindActionArgumentsAsync()
        {
            return _argumentBinder.BindActionArgumentsAsync(Context, Instance);
        }

        // Marking as internal for Unit Testing purposes.
        internal static IActionResult CreateActionResult(Type declaredReturnType, object actionReturnValue)
        {
            if (declaredReturnType == null)
            {
                throw new ArgumentNullException(nameof(declaredReturnType));
            }

            // optimize common path
            var actionResult = actionReturnValue as IActionResult;
            if (actionResult != null)
            {
                return actionResult;
            }

            if (declaredReturnType == typeof(void) ||
                declaredReturnType == typeof(Task))
            {
                return new EmptyResult();
            }

            // Unwrap potential Task<T> types.
            var actualReturnType = GetTaskInnerTypeOrNull(declaredReturnType) ?? declaredReturnType;
            if (actionReturnValue == null &&
                typeof(IActionResult).GetTypeInfo().IsAssignableFrom(actualReturnType.GetTypeInfo()))
            {
                throw new InvalidOperationException(
                    Resources.FormatActionResult_ActionReturnValueCannotBeNull(actualReturnType));
            }

            return new ObjectResult(actionReturnValue)
            {
                DeclaredType = actualReturnType
            };
        }

        private static Type GetTaskInnerTypeOrNull(Type type)
        {
            var genericType = ClosedGenericMatcher.ExtractGenericInterface(type, typeof(Task<>));

            return genericType?.GenericTypeArguments[0];
        }
    }
}
