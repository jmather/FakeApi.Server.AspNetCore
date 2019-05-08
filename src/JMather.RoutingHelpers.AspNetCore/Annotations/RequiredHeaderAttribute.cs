using System;
using System.Collections.Generic;
using JMather.RoutingHelpers.AspNetCore.Constraints;
using Microsoft.AspNetCore.Mvc.ActionConstraints;

namespace JMather.RoutingHelpers.AspNetCore.Annotations
{
    public class RequiredHeaderAttribute : Attribute
    {
        public string Name { get; set; }
        public IEnumerable<string> AllowedValues { get; set; }

        public bool CaseSensitive { get; set; } = false;
        
        public RequiredHeaderAttribute(string name, string allowedValue) 
            : this(name, new List<string> { allowedValue }, false)
        {
        }
        
        public RequiredHeaderAttribute(string name, string allowedValue, bool caseSensitive) 
            : this(name, new List<string> { allowedValue }, caseSensitive)
        {
        }
        
        public RequiredHeaderAttribute(string name, IEnumerable<string> allowedValues, bool caseSensitive)
        {
            Name = name;
            AllowedValues = allowedValues;
            CaseSensitive = caseSensitive;
        }

        public IActionConstraint GetConstraint()
        {
            return new RequiredHeaderConstraint(Name, AllowedValues);
        }

    }
}