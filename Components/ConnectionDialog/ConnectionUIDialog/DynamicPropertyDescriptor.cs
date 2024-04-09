//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Microsoft.Data.ConnectionUI
{
    internal class DynamicPropertyDescriptor : PropertyDescriptor
    {
        private string _name;
        private string _category;
        private string _description;
        private Type _propertyType;
        private string _converterTypeName;
        private TypeConverter _converter;
        private List<Attribute> _attributes;
        private Type _componentType;
        private readonly PropertyDescriptor _baseDescriptor;

        public DynamicPropertyDescriptor(string name)
            : base(name, null)
        {
        }

        public DynamicPropertyDescriptor(string name, params Attribute[] attributes)
            : base(name, FilterAttributes(attributes))
        {
        }

        public DynamicPropertyDescriptor(PropertyDescriptor baseDescriptor)
            : this(baseDescriptor, null)
        {
        }

        public DynamicPropertyDescriptor(PropertyDescriptor baseDescriptor, params Attribute[] newAttributes)
            : base(baseDescriptor, newAttributes)
        {
            AttributeArray = FilterAttributes(AttributeArray);
            _baseDescriptor = baseDescriptor;
        }

        public override string Name
        {
            get
            {
                return _name ?? base.Name;
            }
        }

        public override string Category
        {
            get
            {
                return _category ?? base.Category;
            }
        }

        public override string Description
        {
            get
            {
                return _description ?? base.Description;
            }
        }

        public override Type PropertyType
        {
            get
            {
                if (_propertyType != null)
                {
                    return _propertyType;
                }
                if (_baseDescriptor != null)
                {
                    return _baseDescriptor.PropertyType;
                }
                return null;
            }
        }

        public override bool IsReadOnly
        {
            get
            {
                return ReadOnlyAttribute.Yes.Equals(Attributes[typeof(ReadOnlyAttribute)]);
            }
        }

        public override TypeConverter Converter
        {
            get
            {
                if (_converterTypeName != null)
                {
                    if (_converter == null)
                    {
                        Type converterType = GetTypeFromName(_converterTypeName);
                        if (typeof(TypeConverter).IsAssignableFrom(converterType))
                        {
                            _converter = (TypeConverter)CreateInstance(converterType);
                        }
                    }
                    if (_converter != null)
                    {
                        return _converter;
                    }
                }
                return base.Converter;
            }
        }

        public override AttributeCollection Attributes
        {
            get
            {
                if (_attributes != null)
                {
                    Dictionary<object, Attribute> attributes = new Dictionary<object, Attribute>();
                    foreach (Attribute attr in AttributeArray)
                    {
                        attributes[attr.TypeId] = attr;
                    }
                    foreach (Attribute attr in _attributes)
                    {
                        if (!attr.IsDefaultAttribute())
                        {
                            attributes[attr.TypeId] = attr;
                        }
                        else if (attributes.ContainsKey(attr.TypeId))
                        {
                            attributes.Remove(attr.TypeId);
                        }
                        if (attr is CategoryAttribute categoryAttr)
                        {
                            _category = categoryAttr.Category;
                        }
                        if (attr is DescriptionAttribute descriptionAttr)
                        {
                            _description = descriptionAttr.Description;
                        }
                        if (attr is TypeConverterAttribute typeConverterAttr)
                        {
                            _converterTypeName = typeConverterAttr.ConverterTypeName;
                            _converter = null;
                        }
                    }
                    Attribute[] newAttributes = new Attribute[attributes.Values.Count];
                    attributes.Values.CopyTo(newAttributes, 0);
                    AttributeArray = newAttributes;
                    _attributes = null;
                }
                return base.Attributes;
            }
        }

        public GetValueHandler GetValueHandler { get; set; }

        public SetValueHandler SetValueHandler { get; set; }

        public CanResetValueHandler CanResetValueHandler { get; set; }

        public ResetValueHandler ResetValueHandler { get; set; }

        public ShouldSerializeValueHandler ShouldSerializeValueHandler { get; set; }

        public GetChildPropertiesHandler GetChildPropertiesHandler { get; set; }

        public override Type ComponentType
        {
            get
            {
                if (_componentType != null)
                {
                    return _componentType;
                }
                if (_baseDescriptor != null)
                {
                    return _baseDescriptor.ComponentType;
                }
                return null;
            }
        }

        public void SetName(string value)
        {
            if (value == null)
            {
                value = string.Empty;
            }
            _name = value;
        }

        public void SetDisplayName(string value)
        {
            if (value == null)
            {
                value = DisplayNameAttribute.Default.DisplayName;
            }
            SetAttribute(new DisplayNameAttribute(value));
        }

        public void SetCategory(string value)
        {
            if (value == null)
            {
                value = CategoryAttribute.Default.Category;
            }
            _category = value;
            SetAttribute(new CategoryAttribute(value));
        }

        public void SetDescription(string value)
        {
            if (value == null)
            {
                value = DescriptionAttribute.Default.Description;
            }
            _description = value;
            SetAttribute(new DescriptionAttribute(value));
        }

        public void SetPropertyType(Type value)
        {
            _propertyType = value ?? throw new ArgumentNullException(nameof(value));
        }

        public void SetDesignTimeOnly(bool value)
        {
            SetAttribute(new DesignOnlyAttribute(value));
        }

        public void SetIsBrowsable(bool value)
        {
            SetAttribute(new BrowsableAttribute(value));
        }

        public void SetIsLocalizable(bool value)
        {
            SetAttribute(new LocalizableAttribute(value));
        }

        public void SetIsReadOnly(bool value)
        {
            SetAttribute(new ReadOnlyAttribute(value));
        }

        public void SetConverterType(Type value)
        {
            _converterTypeName = value?.AssemblyQualifiedName;
            if (_converterTypeName != null)
            {
                SetAttribute(new TypeConverterAttribute(value));
            }
            else
            {
                SetAttribute(TypeConverterAttribute.Default);
            }
            _converter = null;
        }

        public void SetAttribute(Attribute value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            (_attributes ?? (_attributes = new List<Attribute>())).Add(value);
        }

        public void SetAttributes(params Attribute[] values)
        {
            foreach (Attribute value in values)
            {
                SetAttribute(value);
            }
        }

        public void SetComponentType(Type value)
        {
            _componentType = value;
        }

        public override object GetValue(object component)
        {
            if (GetValueHandler != null)
            {
                return GetValueHandler(component);
            }
            if (_baseDescriptor != null)
            {
                return _baseDescriptor.GetValue(component);
            }
            return null;
        }

        public override void SetValue(object component, object value)
        {
            if (SetValueHandler != null)
            {
                SetValueHandler(component, value);
                OnValueChanged(component, EventArgs.Empty);
            }
            else if (_baseDescriptor != null)
            {
                _baseDescriptor.SetValue(component, value);
                OnValueChanged(component, EventArgs.Empty);
            }
        }

        public override bool CanResetValue(object component)
        {
            if (CanResetValueHandler != null)
            {
                return CanResetValueHandler(component);
            }
            if (_baseDescriptor != null)
            {
                return _baseDescriptor.CanResetValue(component);
            }
            return Attributes[typeof(DefaultValueAttribute)] != null;
        }

        public override void ResetValue(object component)
        {
            if (ResetValueHandler != null)
            {
                ResetValueHandler(component);
            }
            else if (_baseDescriptor != null)
            {
                _baseDescriptor.ResetValue(component);
            }
            else
            {
                if (Attributes[typeof(DefaultValueAttribute)] is DefaultValueAttribute attribute)
                {
                    SetValue(component, attribute.Value);
                }
            }
        }

        public override bool ShouldSerializeValue(object component)
        {
            if (ShouldSerializeValueHandler != null)
            {
                return ShouldSerializeValueHandler(component);
            }
            if (_baseDescriptor != null)
            {
                return _baseDescriptor.ShouldSerializeValue(component);
            }
            return Attributes[typeof(DefaultValueAttribute)] is DefaultValueAttribute attribute && !object.Equals(GetValue(component), attribute.Value);
        }

        public override PropertyDescriptorCollection GetChildProperties(object instance, Attribute[] filter)
        {
            if (GetChildPropertiesHandler != null)
            {
                return GetChildPropertiesHandler(instance, filter);
            }
            if (_baseDescriptor != null)
            {
                return _baseDescriptor.GetChildProperties(instance, filter);
            }
            return base.GetChildProperties(instance, filter);
        }

        protected override int NameHashCode
        {
            get
            {
                if (_name != null)
                {
                    return _name.GetHashCode();
                }
                return base.NameHashCode;
            }
        }

        private static Attribute[] FilterAttributes(Attribute[] attributes)
        {
            Dictionary<object, Attribute> dictionary = new Dictionary<object, Attribute>();
            foreach (Attribute attribute in attributes)
            {
                if (!attribute.IsDefaultAttribute())
                {
                    dictionary.Add(attribute.TypeId, attribute);
                }
            }
            Attribute[] newAttributes = new Attribute[dictionary.Values.Count];
            dictionary.Values.CopyTo(newAttributes, 0);
            return newAttributes;
        }
    }

    internal delegate object GetValueHandler(object component);

    internal delegate void SetValueHandler(object component, object value);

    internal delegate bool CanResetValueHandler(object component);

    internal delegate void ResetValueHandler(object component);

    internal delegate bool ShouldSerializeValueHandler(object component);

    internal delegate PropertyDescriptorCollection GetChildPropertiesHandler(object instance, Attribute[] filter);
}
