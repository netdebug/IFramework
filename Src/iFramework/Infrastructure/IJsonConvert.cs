﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace IFramework.Infrastructure
{
    public interface IJsonConvert
    {
        string SerializeObject(object value, bool serializeNonPublic = false, bool loopSerialize = false, bool useCamelCase = false, bool ignoreNullValue = false, bool useStringEnumConvert = true);
        object DeserializeObject(string value, bool serializeNonPublic = false, bool loopSerialize = false, bool useCamelCase = false);

        dynamic DeserializeDynamicObject(string json, bool serializeNonPublic = false, bool loopSerialize = false, bool useCamelCase = false);
        dynamic DeserializeDynamicObjects(string json, bool serializeNonPublic = false, bool loopSerialize = false, bool useCamelCase = false);

        T DeserializeAnonymousType<T>(string value, T anonymousTypeObject, bool serializeNonPublic = false, bool loopSerialize = false, bool useCamelCase = false);

        T DeserializeObject<T>(string value, bool serializeNonPublic = false, bool loopSerialize = false, bool useCamelCase = false);

        object DeserializeObject(string value, Type type, bool serializeNonPublic = false, bool loopSerialize = false, bool useCamelCase = false);

        void PopulateObject(string value, object target, bool serializeNonPublic = false, bool loopSerialize = false, bool useCamelCase = false);

        string SerializeXmlNode(XmlNode node);

        XmlDocument DeserializeXmlNode(string value);

        XmlDocument DeserializeXmlNode(string value, string deserializeRootElementName);

        XmlDocument DeserializeXmlNode(string value, string deserializeRootElementName, bool writeArrayAttribute);

        string SerializeXNode(XObject node);

        XDocument DeserializeXNode(string value);

        XDocument DeserializeXNode(string value, string deserializeRootElementName);

        XDocument DeserializeXNode(string value, string deserializeRootElementName, bool writeArrayAttribute);
    }
}
