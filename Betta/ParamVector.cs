// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using System.Collections;
using Betta.Attributes;
using Betta.Services;

namespace Betta
{
    public class ParamVector
    {
        public string Name;
        public string NickName;
        public string Description;
        public bool Optional;
        public GH_ParamAccess ParamAccessType;
        public object DefaultValue;

        public Type ParameterType;  // Made public
        public List<CustomAttributeData> Attributes; //Here?

        public ParamVector(ParameterInfo parameter)
        {
            ParameterType = parameter.ParameterType;
            Attributes = parameter.GetCustomAttributesData().ToList();

            var attr = parameter.GetCustomAttribute<GrasshopperParameterAttribute>();
            Name = attr?.Name ?? parameter.Name;
            NickName = attr?.NickName ?? attr?.Name ?? parameter.Name;
            Description = attr?.Description ?? parameter.Name;
            Optional = attr?.Optional ?? false;
            DefaultValue = attr?.DefaultValue;

            ParamAccessType = GetParamAccessType(parameter.ParameterType);
        }

        public GH_ParamAccess GetParamAccessType(Type type)
        {
            type = type ?? ParameterType;
            
            if (type == null) return GH_ParamAccess.item;

            if (type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(GH_Structure<>) &&
                typeof(IGH_Goo).IsAssignableFrom(type.GetGenericArguments()[0])) {
                return GH_ParamAccess.tree; // -> tes this
            }

            // Strings must be treated as item
            if (type == typeof(string))
                return GH_ParamAccess.item;

            if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
                return GH_ParamAccess.list;
            else {
                return GH_ParamAccess.item;
            }
        }

        public static GH_ParamAccess SolveAccessType<T>(Type parameterType)
        {
            if (parameterType is IEnumerable<T>) {
                return GH_ParamAccess.list;
            }
            else {
                return GH_ParamAccess.item;
            }
            //TODO add tree?
        }

        public ParamVector(string name, string nickName, string description, bool optional, GH_ParamAccess paramAccessType)
        {
            Name = name;
            NickName = nickName;
            Description = description;
            Optional = optional;
            ParamAccessType = paramAccessType;
        }

        public IGH_Param GetGhParamType()
        {
            if (ParameterType != null) {
                if (typeof(IList).IsAssignableFrom(ParameterType)) {  //Using list here since string is IEnnumerable<char> it will land here -> is more type of collection needed add specific type or interface
                    Type TypeOfT = GetAnyElementType(ParameterType);
                    return GetGhParamType(TypeOfT);
                }
            }
            return GetGhParamType(ParameterType);
        }


        public static Type GetAnyElementType(Type type)
        {
            // Type is Array
            // short-circuit if you expect lots of arrays 

            /*
            if (type.IsArray)
                return type.GetElementType();
            */ //String is array of char

            // type is IEnumerable<T>;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return type.GetGenericArguments()[0];

            // type implements/extends IEnumerable<T>;
            var enumType = type.GetInterfaces()
                                    .Where(t => t.IsGenericType &&
                                           t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                                    .Select(t => t.GenericTypeArguments[0]).FirstOrDefault();
            return enumType ?? type;
        }

        public static IGH_Param GetGhParamType(System.Type type)
        {
            if (type != null) {
                // Opaque domain types come first: ComponentRegistry registered
                // a Param_BettaGoo<T> factory at discovery, so any input or
                // output typed as T (or List<T>, since the instance overload
                // strips the IList wrapper) gets the typed param + goo pair.
                if (ParamRegistry.TryGetFactory(type, out var opaqueFactory))
                    return opaqueFactory();

                return type.FullName switch
                {
                    "Rhino.Geometry.Point3d" => new Param_Point(),
                    "Rhino.Geometry.Polyline" => new Param_Curve(),
                    "Rhino.Geometry.Vector3d" => new Param_Vector(),
                    "Rhino.Geometry.Circle" => new Param_Circle(),
                    "Rhino.Geometry.Plane" => new Param_Plane(),
                    "Rhino.Geometry.Line" => new Param_Line(),
                    "Rhino.Geometry.Arc" => new Param_Arc(),
                    "Rhino.Geometry.Box" => new Param_Box(),
                    "Rhino.Geometry.BoundingBox" => new Param_Box(),
                    "Rhino.Geometry.Brep" => new Param_Brep(),
                    "Rhino.Geometry.Mesh" => new Param_Mesh(),
                    "Rhino.Geometry.Surface" => new Param_Surface(),
                    "Rhino.Geometry.Curve" => new Param_Curve(),
                    "Rhino.Geometry.Interval" => new Param_Interval(),
                    "Rhino.Geometry.Transform" => new Param_Transform(),

                    "System.Drawing.Color" => new Param_Colour(),
                    "System.Guid" => new Param_Guid(),

                    // Bitmap / Image are container types with a large public
                    // property surface (Width, Height, PixelFormat, Palette,
                    // Flags, HorizontalResolution…). Without this branch,
                    // OutputPlanner's "class-return explosion" would emit
                    // one output per property — nonsense for anyone actually
                    // producing an image. Map to Param_GenericObject so the
                    // bitmap flows as one wire; consumers unwrap with a cast.
                    // For richer ergonomics (bake-to-PictureFrame, thumbnail,
                    // PNG serialize) return BettaImage from Betta.Preview
                    // instead — it's opaque and gets its own typed goo pair.
                    "System.Drawing.Bitmap" => new Param_GenericObject(),
                    "System.Drawing.Image" => new Param_GenericObject(),

                    _ => GetGhParamGenericType(type),
                };
            }
            /*
        if (fullname == "Rhino.Geometry.Point3d") {
                return new Param_Point();
        }
        else if (fullname == "Rhino.Geometry.Polyline") {
            return new Param_Curve();
        }
        else if (type is Vector3d) {
            return new Param_Vector();
        }


            else {
            return GetGhParamGenericType(type);
        }
        */
            else {
                return new Param_GenericObject();
            }
        }

        public static IGH_Param GetGhParamGenericType(Type type)
        {
            // Enums are integer-backed at the CLR level — surface as an
            // integer input. The method body still receives the typed enum
            // value (ParamInjector.ChangeTypeStrong converts int → enum).
            // Users can wire an Integer Slider or a Panel ("Walls" parses too).
            if (type != null && type.IsEnum) return new Param_Integer();

            switch (Type.GetTypeCode(type)) {

                //system types
                case TypeCode.Int32: return new Param_Integer();
                case TypeCode.Int16: return new Param_Integer();
                case TypeCode.Double: return new Param_Number();
                case TypeCode.String: return new Param_String();
                case TypeCode.Boolean: return new Param_Boolean();
                case TypeCode.Object: return new Param_GenericObject();

                //generic object
                default: return new Param_GenericObject();
            }
            throw (new Exception("Type not supported"));
        }




        // Back-compat overload: no method context → generic output names only.
        public static List<IGH_Param> GetOutputs(Type outputType) => GetOutputs(outputType, null);

        /// <summary>
        /// Build the output IGH_Params for a method's return type. Output names
        /// are resolved by <see cref="OutputPlanner.PlanOutputs"/> (which is
        /// GH-free and unit-tested); this method just maps each plan to a
        /// concrete param via <see cref="ParamVectorExtensions.ToGhParam"/>.
        /// </summary>
        public static List<IGH_Param> GetOutputs(Type outputType, MethodInfo method)
        {
            var listOutput = new List<IGH_Param>();
            foreach (var plan in OutputPlanner.PlanOutputs(outputType, method))
                listOutput.Add(MakeOutput(
                    plan.Type,
                    plan.IsList ? GH_ParamAccess.list : GH_ParamAccess.item,
                    plan.Name, plan.NickName, plan.Description));
            return listOutput;
        }

        private static IGH_Param MakeOutput(Type type, GH_ParamAccess access, string name, string nickName, string description)
        {
            var param = new ParamVector(name, nickName, description, false, access)
            {
                ParameterType = type
            };
            return param.ToGhParam();
        }
    }

    public static class ParamVectorExtensions
    {
        /// <summary>
        /// Converts ParamVector to proper GH_Pram
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        public static IGH_Param ToGhParam(this ParamVector param)
        {
            IGH_Param ghParam = param.GetGhParamType();
            ghParam.Name = param.Name;
            ghParam.NickName = param.NickName;
            ghParam.Description = param.Description;
            ghParam.Access = param.GetParamAccessType(param.ParameterType);
            ghParam.Optional = param.Optional;

            if (param.DefaultValue != null)
                ApplyDefault(ghParam, param.DefaultValue);

            return ghParam;
        }

        /// <summary>
        /// Seed the GH input's persistent data so an unwired slot carries the
        /// user's declared default instead of default(T). Supports the
        /// primitive Param_* types; other param types fall through without
        /// erroring (the default simply won't appear in the UI).
        /// </summary>
        private static void ApplyDefault(IGH_Param ghParam, object value)
        {
            try
            {
                switch (ghParam)
                {
                    case Param_Number pn when value is IConvertible:
                        pn.PersistentData.Append(new GH_Number(Convert.ToDouble(value)));
                        break;
                    case Param_Integer pi when value is IConvertible:
                        pi.PersistentData.Append(new GH_Integer(Convert.ToInt32(value)));
                        break;
                    case Param_String ps:
                        ps.PersistentData.Append(new GH_String(value.ToString()));
                        break;
                    case Param_Boolean pb when value is IConvertible:
                        pb.PersistentData.Append(new GH_Boolean(Convert.ToBoolean(value)));
                        break;
                }
            }
            catch
            {
                // Bad default value types are a non-fatal authoring mistake;
                // the component still works without the default.
            }
        }
    }
}


/// <summary>
/// Paramfrom reflection to IGH
/// </summary>


//Get T from List
/*
Type type = pi.PropertyType;
if (type.IsGenericType && type.GetGenericTypeDefinition()
        == typeof(List<>)) {
    Type itemType = type.GetGenericArguments()[0]; // use this...
}

foreach (Type interfaceType in type.GetInterfaces()) {
    if (interfaceType.IsGenericType &&
        interfaceType.GetGenericTypeDefinition()
        == typeof(IList<>)) {
        Type itemType = type.GetGenericArguments()[0];
        // do something...
        break;
    }
}
*/