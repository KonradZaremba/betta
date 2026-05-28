// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Dynamic;

namespace Betta
{
    public static class ExpandoExtentions
    {

        public static List<dynamic> GetOrderdDataBy(this ExpandoObject expandoObject, List<string> ordredNames)
        {
            return expandoObject.OrderBy(x => ordredNames.IndexOf(x.Key)).Select(x => x.Value).ToList();
        }


        //This should be in vector?
        public static ExpandoObject ToExpando(this List<ParameterInfo> parameterInfos)
        {
            var expandoObject = new ExpandoObject();

            var expandoDict = (IDictionary<string, dynamic>)expandoObject;

            expandoDict["Status"] = ExpandoStatus.Initilized;

            //Stores Data Types
            expandoDict["Types"] = new Dictionary<string, Type>();
            
            List<dynamic> listOfTypes = new List<dynamic>();

            foreach (var parameterInfo in parameterInfos) {

                if (parameterInfo.ParameterType.IsValueType) {
                    listOfTypes.Add((parameterInfo.Name, ParamVector.GetAnyElementType(parameterInfo.ParameterType)));
                    expandoDict[parameterInfo.Name] = Activator.CreateInstance(parameterInfo.ParameterType);
                }
                else {
                    listOfTypes.Add((parameterInfo.Name, parameterInfo.ParameterType));
                    expandoDict[parameterInfo.Name] = new List<dynamic>() ;
                }
            }
            expandoDict["Types"] = listOfTypes;

            return expandoObject;
        }
     
        enum ExpandoStatus
        {
            Initilized = 0,
            HasValues = 1
        } 

        public static object[] ToArray()
        {
            object[] array = null;



            return array;
        }

        public static ExpandoObject Copy(this ExpandoObject original)
        {
            var expandoClone = new ExpandoObject();
            var expandoCloneDict = (IDictionary<string, dynamic>)expandoClone;
            var originalDict = (IDictionary<string, dynamic>)original;

            foreach (var kvp in originalDict) {
                expandoCloneDict[kvp.Key] = kvp.Value;
            }
            
            return expandoClone;
        }

        public static bool IsEqual(this ExpandoObject expando1, ExpandoObject expando2)
        {
            // Get dictionaries for both expando objects
            var dict1 = (IDictionary<string, dynamic>)expando1;
            var dict2 = (IDictionary<string, dynamic>)expando2;

            // Check if number of properties match
            if (dict1.Count != dict2.Count) {
                return false;
            }

            // Check if all properties match
            foreach (var key in dict1.Keys) {
                // Check if dict2 has the same key
                if (!dict2.ContainsKey(key)) {
                    return false;
                }

                // Check if the values are equal
                if (!dict1[key].Equals(dict2[key])) {
                    return false;
                }
            }

            // Expando objects match
            return true;
        }

    }
}