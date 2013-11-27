﻿using System.Reflection;
using System;
using System.Collections.Generic;
using Castle.DynamicProxy;
using Microsoft.SharePoint;
using SharepointCommon.Common;
using SharepointCommon.Impl;

namespace SharepointCommon.Interception
{
    internal sealed class LookupAccessInterceptor : IInterceptor
    {
        private readonly SPListItem _listItem;
        private List<string> _changedFields;

        public LookupAccessInterceptor(SPListItem listItem)
        {
            _listItem = listItem;
            _changedFields = new List<string>();
        }

        public void Intercept(IInvocation invocation)
        {
            if (invocation.Method.Name.StartsWith("set_"))
            {
                _changedFields.Add(invocation.Method.Name.Substring(4));
                invocation.Proceed();
                return;
            }
            if (invocation.Method.Name.StartsWith("get_"))
            {
                if (_changedFields.Contains(invocation.Method.Name.Substring(4)))
                {
                    invocation.Proceed();
                    return;
                }

                if (typeof(Item).IsAssignableFrom(invocation.Method.ReturnType))
                {
                    invocation.ReturnValue = GetLookupItem(invocation.Method);
                    return;
                }
            }
            invocation.Proceed();
        }

        private object GetLookupItem(MethodInfo memberInfo)
        {
            var wf = new QueryWeb(_listItem.ParentList.ParentWeb);
            var list = wf.Web.Lists[_listItem.ParentList.ID];
            var listItem = list.GetItemById(_listItem.ID);

            var ft = FieldMapper.ToFieldType(memberInfo);
            var lookupField = listItem.Fields.TryGetFieldByStaticName(ft.Name) as SPFieldLookup;

            if (lookupField == null)
            {
                throw new SharepointCommonException(string.Format("cant find '{0}' field in list '{1}'", ft.Name, listItem.ParentList.Title));
            }

            if (string.IsNullOrEmpty(lookupField.LookupList))
            {
                throw new SharepointCommonException(string.Format("lookup field {0} in [{1}] cannot find lookup list", lookupField.InternalName, list.RootFolder.Url));
            }

            var lookupList = wf.Web.Lists[new Guid(lookupField.LookupList)];

            // Lookup with picker (ilovesharepoint) returns SPFieldLookupValue
            var fieldValue = listItem[ft.Name];
            var lkpValue = fieldValue as SPFieldLookupValue ?? new SPFieldLookupValue((string)fieldValue ?? string.Empty);
            if (lkpValue.LookupId == 0) return null;

            SPListItem lookupItem;
            try
            {
                lookupItem = lookupList.GetItemById(lkpValue.LookupId);
            }
            catch
            {
                return null;
            }

            return EntityMapper.ToEntity(memberInfo.ReturnType, lookupItem);
        }
    }
}