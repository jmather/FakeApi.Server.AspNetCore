using System;
using System.Collections.Generic;
using JMather.RoutingHelpers.AspNetCore.Constraints;
using Microsoft.AspNetCore.Mvc.ActionConstraints;

namespace JMather.RoutingHelpers.AspNetCore.Annotations
{
    public class ParameterMustContainAttribute : Attribute
    {
        public string ParameterName { get; set; }
        public IEnumerable<object> AllowedValues { get; set; }

        public ParameterMustContainAttribute(string parameterName, string allowedValue) 
            : this(parameterName, new List<object> { allowedValue })
        {
        }
        
        public ParameterMustContainAttribute(string parameterName, IEnumerable<object> allowedValues)
        {
            ParameterName = parameterName;
            AllowedValues = allowedValues;
        }

        public IActionConstraint GetConstraint()
        {
            return new ParameterMustContainConstraint(ParameterName, AllowedValues);
        }
    }
}