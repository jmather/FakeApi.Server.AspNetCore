using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace JMather.RoutingHelpers.AspNetCore.Constraints
{
    public class ParameterMustContainConstraint : IActionConstraint
    {
        private readonly string _parameterName;
        private readonly IEnumerable<object> _allowedValues;
        
        public ParameterMustContainConstraint(string parameterName, IEnumerable<object> allowedValues)
        {
            _parameterName = parameterName;
            _allowedValues = allowedValues;
        }
        
        public bool Accept(ActionConstraintContext context)
        {
            var paramList = context.CurrentCandidate.Action.Parameters;

            object value = null;

            foreach (var param in paramList)
            {
                if (param.Name != _parameterName)
                {
                    continue;
                }

                value = GetParameterValue(param, context);
                break;
            }

            if (value == null)
            {
                return false;
            }
            
            return _allowedValues.Any(allowedValue => string.Equals(allowedValue, value));
        }

        private object GetParameterValue(ParameterDescriptor parameterDescriptor, ActionConstraintContext context)
        {
            if (parameterDescriptor.BindingInfo.BindingSource == BindingSource.Header)
            {
                return Enumerable.FirstOrDefault<string>(context.RouteContext.HttpContext.Request.Headers
                        .Where(h => String.Equals(h.Key, parameterDescriptor.BindingInfo.BinderModelName,
                            StringComparison.CurrentCultureIgnoreCase))
                        .Select(h => h.Value.ToString()));
            }

            return null;
        }

        public int Order => 0;
    }
}