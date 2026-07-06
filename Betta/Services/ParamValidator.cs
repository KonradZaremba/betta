// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Betta.Attributes;
using Betta.Interfaces;

namespace Betta.Services
{
    /// <summary>
    /// Thrown by ParamValidator when a wired input fails a declared check —
    /// caught by BettaComponent and surfaced as a Warning (not an Error) so
    /// the solve can complete with the next valid input. The method body is
    /// skipped for the offending invocation.
    /// </summary>
    public class BettaValidationException : Exception
    {
        public BettaValidationException(string message) : base(message) { }
    }

    /// <summary>
    /// Runs declared parameter validations ([GrasshopperRange],
    /// [GrasshopperNotEmpty], [GrasshopperValidation]) for a single
    /// invocation's argument vector. Returns the first failure's message;
    /// null if every parameter passes.
    /// </summary>
    public static class ParamValidator
    {
        public static string Validate(IList<ParameterInfo> parameters, object[] args)
        {
            if (parameters == null || args == null) return null;
            for (int i = 0; i < parameters.Count && i < args.Length; i++)
            {
                var msg = ValidateOne(parameters[i], args[i]);
                if (msg != null) return msg;
            }
            return null;
        }

        private static string ValidateOne(ParameterInfo p, object value)
        {
            var range = p.GetCustomAttribute<GrasshopperRangeAttribute>();
            if (range != null && value != null)
            {
                if (value is IConvertible)
                {
                    try
                    {
                        var d = Convert.ToDouble(value);
                        if (d < range.Min || d > range.Max)
                            return $"{p.Name}: {d} is outside [{range.Min}, {range.Max}]";
                    }
                    catch { /* not numeric — let other validators or the method body deal */ }
                }
            }

            if (p.GetCustomAttribute<GrasshopperNotEmptyAttribute>() != null)
            {
                if (value == null) return $"{p.Name}: required, but no value was wired";
                if (value is string s && string.IsNullOrWhiteSpace(s))
                    return $"{p.Name}: required, but the wired string is empty";
                if (value is IEnumerable enumerable && value is not string)
                {
                    bool any = false;
                    foreach (var _ in enumerable) { any = true; break; }
                    if (!any) return $"{p.Name}: required, but the wired list is empty";
                }
            }

            var customs = p.GetCustomAttributes<GrasshopperValidationAttribute>().ToList();
            foreach (var a in customs)
            {
                if (a.ValidatorType == null) continue;
                if (!typeof(IBettaValidator).IsAssignableFrom(a.ValidatorType)) continue;
                try
                {
                    var validator = (IBettaValidator)Activator.CreateInstance(a.ValidatorType);
                    var msg = validator.Validate(value);
                    if (!string.IsNullOrEmpty(msg))
                        return $"{p.Name}: {msg}";
                }
                catch (Exception ex)
                {
                    return $"{p.Name}: validator {a.ValidatorType.Name} threw — {ex.Message}";
                }
            }

            return null;
        }
    }
}
