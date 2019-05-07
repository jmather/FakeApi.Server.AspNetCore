using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace JMather.RoutingHelpers.AspNetCore.Constraints
{
    public class RequiredHeaderConstraint : IActionConstraint
    {
        private readonly string _name;
        private readonly IEnumerable<string> _allowedValues;
        private readonly bool _caseSensitive;
        
        public RequiredHeaderConstraint(string name, IEnumerable<string> allowedValues, bool caseSensitive = false)
        {
            _name = name;
            _allowedValues = allowedValues;
            _caseSensitive = caseSensitive;
        }
        
        public bool Accept(ActionConstraintContext context)
        {
            var values = context.RouteContext.HttpContext.Request.Headers
                .Where(h => String.Equals(h.Key, _name, StringComparison.CurrentCultureIgnoreCase))
                .Select(h => h.Value.ToString())
                .ToList();
            
            if (values.Count == 0)
            {
                return false;
            }

            var compareType = (_caseSensitive) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            
            return _allowedValues
                .Any(allowedValue =>
                {
                    return values
                        .Any(providedValue => string.Equals(allowedValue, providedValue, compareType));
                });
        }
        
        public int Order => 0;
    }
}