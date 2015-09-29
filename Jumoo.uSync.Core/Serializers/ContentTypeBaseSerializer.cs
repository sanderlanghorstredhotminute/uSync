﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Services;

using Jumoo.uSync.Core.Interfaces;
using Jumoo.uSync.Core.Extensions;
using Umbraco.Core.Logging;
using Umbraco.Core.Models.EntityBase;

namespace Jumoo.uSync.Core.Serializers
{
    abstract public class ContentTypeBaseSerializer<T> : SyncBaseSerializer<T>, ISyncSerializerTwoPass<T>
    {
        internal IContentTypeService _contentTypeService;
        internal IDataTypeService _dataTypeService;
        internal IMemberTypeService _memberTypeService; 

        public ContentTypeBaseSerializer(string itemType): base(itemType)
        {
            _contentTypeService = ApplicationContext.Current.Services.ContentTypeService;
            _dataTypeService = ApplicationContext.Current.Services.DataTypeService;
            _memberTypeService = ApplicationContext.Current.Services.MemberTypeService;
        }

        #region ContentTypeBase Deserialize Helpers

        /// <summary>
        ///  does the basic deserialization, bascially the stuff in info
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        internal void DeserializeBase(IContentTypeBase item, XElement info)
        {
            var alias = info.Element("Alias").Value;
            if (item.Alias != alias)
                item.Alias = alias;

            var name = info.Element("Name").ValueOrDefault(string.Empty);
            if (!string.IsNullOrEmpty(name) )
            {
                item.Name = name;
            }

            var icon = info.Element("Icon").ValueOrDefault("");
            if (item.Icon != icon)
                item.Icon = icon;

            var thumb = info.Element("Thumbnail").ValueOrDefault("");
            if (item.Thumbnail != thumb)
                item.Thumbnail = thumb;

            var desc = info.Element("Description").ValueOrDefault("");
            if (item.Description != desc)
                item.Description = desc;

            var allow = info.Element("AllowAtRoot").ValueOrDefault(false);
            if (item.AllowedAsRoot != allow)
                item.AllowedAsRoot = allow;

            var masterNode = info.Element("Master");
            if (masterNode != null)
            {
                var masterId = 0;

                var masterKey = masterNode.Attribute("Key").ValueOrDefault(Guid.Empty);
                if (masterKey != Guid.Empty)
                {
                    var masterEntity = ApplicationContext.Current.Services.EntityService.GetByKey(masterKey);
                    masterId = masterEntity.Id;
                }

                if (masterId == 0)
                {
                    // old school alias lookup
                    var master = default(IContentTypeBase);

                    LogHelper.Debug<Events>("Looking up Content Master by Alias");
                    var masterAlias = masterNode.Value;
                    master = LookupByAlias(masterAlias);
                    if (master != null)
                        masterId = master.Id;
                }

                if (masterId > 0)
                {
                    item.SetLazyParentId(new Lazy<int>(() => masterId));                        
                }
            }
        }

        internal void DeserializeStructure(IContentTypeBase item, XElement node)
        {
            var structureNode = node.Element("Structure");
            if (structureNode == null)
                return;

            List<ContentTypeSort> allowedTypes = new List<ContentTypeSort>();
            int sortOrder = 0;

            foreach (var contentBaseNode in structureNode.Elements(_itemType))
            {
                var alias = contentBaseNode.Value;
                var key = contentBaseNode.Attribute("Key").ValueOrDefault(Guid.Empty);

                IContentTypeBase contentBaseItem = default(IContentTypeBase);
                IUmbracoEntity baseItem = default(IUmbracoEntity);

                var _entityService = ApplicationContext.Current.Services.EntityService;

                if (key != Guid.Empty)
                {
                    LogHelper.Debug<uSync.Core.Events>("Using key to find structure element");
                    contentBaseItem = LookupByKey(key);
                }

                if (baseItem == null && !string.IsNullOrEmpty(alias))
                {
                    LogHelper.Debug<uSync.Core.Events>("Fallback Alias lookup");
                    contentBaseItem = LookupByAlias(alias);
                }

                if (contentBaseItem != default(IContentTypeBase))
                {
                    allowedTypes.Add(new ContentTypeSort(
                        new Lazy<int>(() => contentBaseItem.Id), sortOrder, contentBaseItem.Name));
                    sortOrder++;
                }
            }

            item.AllowedContentTypes = allowedTypes;
        }

        internal void DeserializeProperties(IContentTypeBase item, XElement node)
        {
            List<string> propertiesToRemove = new List<string>();
            Dictionary<string, string> propertiesToMove = new Dictionary<string, string>();
            Dictionary<PropertyGroup, string> tabsToBlank = new Dictionary<PropertyGroup, string>();

            var genericPropertyNode = node.Element("GenericProperties");
            if (genericPropertyNode != null)
            {
                // add or update properties
                foreach (var propertyNode in genericPropertyNode.Elements("GenericProperty"))
                {
                    bool newProperty = false; 

                    var property = default(PropertyType);
                    var propKey = propertyNode.Element("Key").ValueOrDefault(Guid.Empty);
                    if (propKey != Guid.Empty)
                    {
                        property = item.PropertyTypes.SingleOrDefault(x => x.Key == propKey);
                    }

                    var alias = propertyNode.Element("Alias").ValueOrDefault(string.Empty);

                    if (property == null)
                    {
                        // look up via alias?
                        property = item.PropertyTypes.SingleOrDefault(x => x.Alias == alias);
                    }

                    // we need to get element stuff now before we can create or update

                    var defGuid = propertyNode.Element("Definition").ValueOrDefault(Guid.Empty);
                    var dataTypeDefinition = _dataTypeService.GetDataTypeDefinitionById(defGuid);

                    if (dataTypeDefinition == null)
                    {
                        var propEditorAlias = propertyNode.Element("Type").ValueOrDefault(string.Empty);
                        if (!string.IsNullOrEmpty(propEditorAlias))
                        {
                            dataTypeDefinition = _dataTypeService
                                            .GetDataTypeDefinitionByPropertyEditorAlias(propEditorAlias)
                                            .FirstOrDefault();
                        }
                    }

                    if (dataTypeDefinition == null)
                    { 
                        LogHelper.Warn<Events>("Failed to get Definition for property type");
                        continue;
                    }

                    if (property == null)
                    {
                        // create the property
                        LogHelper.Debug<Events>("Creating new Property: {0} {1}", ()=> item.Alias,  ()=> alias);
                        property = new PropertyType(dataTypeDefinition, alias);
                        newProperty = true;
                    }

                    if (property != null)
                    {
                        LogHelper.Debug<Events>("Updating Property :{0} {1}", ()=> item.Alias, ()=> alias);

                        var key = propertyNode.Element("Key").ValueOrDefault(Guid.Empty);
                        if (key != Guid.Empty)
                        {
                            LogHelper.Debug<Events>("Setting Key :{0}", () => key);
                            property.Key = key;
                        }

                        LogHelper.Debug<Events>("Item Key    :{0}", () => property.Key);

                        // update settings.
                        property.Name = propertyNode.Element("Name").ValueOrDefault("unnamed" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));

                        if (property.Alias != alias)
                            property.Alias = alias; 

                        if (propertyNode.Element("Description") != null)
                            property.Description = propertyNode.Element("Description").Value;

                        if (propertyNode.Element("Mandatory") != null)
                            property.Mandatory = propertyNode.Element("Mandatory").Value.ToLowerInvariant().Equals("true");

                        if (propertyNode.Element("Validation") != null)
                            property.ValidationRegExp = propertyNode.Element("Validation").Value;

                        if (propertyNode.Element("SortOrder") != null)
                            property.SortOrder = int.Parse(propertyNode.Element("SortOrder").Value);

                        if (propertyNode.Element("Type") != null)
                        {
                            LogHelper.Debug<Events>("Setting Property Type : {0}", () => propertyNode.Element("Type").Value);
                            property.PropertyEditorAlias = propertyNode.Element("Type").Value;
                        }

                        if (property.DataTypeDefinitionId != dataTypeDefinition.Id)
                        {
                            property.DataTypeDefinitionId = dataTypeDefinition.Id;
                        }

                        var tabName = propertyNode.Element("Tab").ValueOrDefault(string.Empty);

                        if (_itemType == "MemberType")
                        {
                            ((IMemberType)item).SetMemberCanEditProperty(alias,
                                propertyNode.Element("CanEdit").ValueOrDefault(false));

                            ((IMemberType)item).SetMemberCanViewProperty(alias,
                                propertyNode.Element("CanView").ValueOrDefault(false));
                        }

                        if (!newProperty)
                        {
                            if (!string.IsNullOrEmpty(tabName))
                            {
                                var propGroup = item.PropertyGroups.FirstOrDefault(x => x.Name == tabName);
                                if (propGroup != null)
                                {
                                    if (!propGroup.PropertyTypes.Any(x => x.Alias == property.Alias))
                                    {
                                        // this tab currently doesn't contain this property, to we have to
                                        // move it (later)
                                        propertiesToMove.Add(property.Alias, tabName);
                                    }
                                }
                            }
                            else
                            {
                                // this property isn't in a tab (now!)
                                if (!newProperty)
                                {
                                    var existingTab = item.PropertyGroups.FirstOrDefault(x => x.PropertyTypes.Contains(property));
                                    if (existingTab != null)
                                    {
                                        // this item is now not in a tab (when it was)
                                        // so we have to remove it from tabs (later)
                                        tabsToBlank.Add(existingTab, property.Alias);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // new propert needs to be added to content type..
                            if (string.IsNullOrEmpty(tabName))
                            {
                                item.AddPropertyType(property);
                            }
                            else
                            {
                                item.AddPropertyType(property, tabName);
                            }

                            // setting the key before here doesn't seem to work for new types.
                            if (key != Guid.Empty)
                                property.Key = key; 
                        }
                    }
                } // end foreach property
            } // end generic properties 


            // look at what properties we need to remove. 
            var propertyNodes = node.Elements("GenericProperties").Elements("GenericProperty");
            foreach(var property in item.PropertyTypes)
            {
                XElement propertyNode = propertyNodes
                                            .FirstOrDefault(x => x.Element("Key").Value == property.Key.ToString());

                if (propertyNode == null)
                {
                    LogHelper.Debug<uSync.Core.Events>("Looking up property type by alias {0}", ()=> property.Alias);
                    propertyNode = propertyNodes
                        .SingleOrDefault(x => x.Element("Alias").Value == property.Alias);
                }

                if (propertyNode == null)
                {
                    propertiesToRemove.Add(property.Alias);
                }
            }


            // now we have gone through all the properties, we can do the moves and removes from the groups
            if (propertiesToMove.Any())
            {
                foreach (var move in propertiesToMove)
                {
                    LogHelper.Debug<Events>("Moving Property: {0} {1}", () => move.Key, () => move.Value);
                    item.MovePropertyType(move.Key, move.Value);
                }
            }

            if (propertiesToRemove.Any())
            {
                // removing properties can cause timeouts on installs with lots of content...
                foreach(var delete in propertiesToRemove)
                {
                    LogHelper.Debug<Events>("Removing Property: {0}", () => delete);
                    item.RemovePropertyType(delete);
                }
            }

            if (tabsToBlank.Any())
            {
                foreach(var blank in tabsToBlank)
                {
                    // there might be a bug here, we need to do some cheking of if this is 
                    // possible with the public api

                    // blank.Key.PropertyTypes.Remove(blank.Value);
                }
            }

        }

        internal void DeserializeTabSortOrder(IContentTypeBase item, XElement node)
        {
            var tabNode = node.Element("Tabs");

            foreach(var tab in tabNode.Elements("Tab"))
            {
                var name = tab.Element("Caption").Value;
                var sortOrder = tab.Element("SortOrder");

                if (sortOrder != null)
                {
                    if (!string.IsNullOrEmpty(sortOrder.Value))
                    {
                        var itemTab = item.PropertyGroups.FirstOrDefault(x => x.Name == name);
                        if (itemTab != null)
                        {
                            itemTab.SortOrder = int.Parse(sortOrder.Value);
                        }
                        else
                        {
                            LogHelper.Debug<Events>("Adding new Tab? {0}", ()=> name);
                            // at this point we might have a missing tab. 
                            if (item.AddPropertyGroup(name))
                            {
                                itemTab = item.PropertyGroups.FirstOrDefault(x => x.Name == name);
                                if (itemTab != null)
                                    itemTab.SortOrder = int.Parse(sortOrder.Value);
                            }
                        }
                    }
                }
            }

            // remove tabs 
            List<string> tabsToRemove = new List<string>();
            foreach(var tab in item.PropertyGroups)
            {
                if (tabNode.Elements("Tab").FirstOrDefault(x => x.Element("Caption").Value == tab.Name) == null)
                {
                    // no tab of this name in the import... remove it.
                    tabsToRemove.Add(tab.Name);
                }
            }

            foreach (var name in tabsToRemove)
            {
                item.PropertyGroups.Remove(name);
            }            
        }
#endregion

#region ContentTypeBase Serialize Helpers
        internal XElement SerializeInfo(IContentTypeBase item)
        {
            var info = new XElement("Info",
                            new XElement("Key", item.Key),
                            new XElement("Name", item.Name),
                            new XElement("Alias", item.Alias),
                            new XElement("Icon", item.Icon),
                            new XElement("Thumbnail", item.Thumbnail),
                            new XElement("Description", string.IsNullOrEmpty(item.Description) ? "" : item.Description ),
                            new XElement("AllowAtRoot", item.AllowedAsRoot.ToString()),
                            new XElement("IsListView", item.IsContainer.ToString()));

            return info;
        }

        internal XElement SerializeTabs(IContentTypeBase item)
        {
            var tabs = new XElement("Tabs");
            foreach (var tab in item.PropertyGroups.OrderBy(x => x.SortOrder))
            {
                tabs.Add(new XElement("Tab",
                        // new XElement("Key", tab.Key),
                        new XElement("Caption", tab.Name),
                        new XElement("SortOrder", tab.SortOrder)));
            }

            return tabs;
        }

        /// <summary>
        ///  So fiddling with the structure
        /// 
        ///  In an umbraco export the structure can come out in a random order
        ///  for consistancy, and better tracking of changes we export the list
        ///  in alias order, that way it should always be the same every time
        ///  regardless of the creation order of the doctypes.
        /// 
        ///  In earlier versions of umbraco, the structure export didn't always
        ///  work - so we redo the export, if it turns out this is fixed in 7.3
        ///  we shoud just do the xml sort like with properties, it will be faster
        /// </summary>
        internal XElement SerializeStructure(IContentTypeBase item)
        {
            var structureNode = new XElement("Structure");

            LogHelper.Debug<Events>("BASE: Content Types: {0}", () => item.AllowedContentTypes.Count());

            SortedList<string, Guid> allowedAliases = new SortedList<string, Guid>();
            foreach(var allowedType in item.AllowedContentTypes)
            {
                IContentTypeBase allowed = LookupById(allowedType.Id.Value);
                if (allowed != null)
                    allowedAliases.Add(allowed.Alias, allowed.Key);
            }


            foreach (var alias in allowedAliases)
            {
                structureNode.Add(new XElement(_itemType, alias.Key,
                    new XAttribute("Key", alias.Value.ToString()))
                    );
            }
            return structureNode;            
        }

        /// <summary>
        ///  as with structure, we want to export properties in a consistant order
        ///  this just jiggles the order of the generic properties section, ordering by name
        /// 
        ///  at the moment we are making quite a big assumption that name is always there?
        /// </summary>
        internal XElement SerializeProperties(IContentTypeBase item)
        {
            var _dataTypeService = ApplicationContext.Current.Services.DataTypeService;

            var properties = new XElement("GenericProperties");

            foreach(var property in item.PropertyTypes.OrderBy(x => x.Name))
            {
                var propNode = new XElement("GenericProperty");

                propNode.Add(new XElement("Key", property.Key));
                propNode.Add(new XElement("Name", property.Name));
                propNode.Add(new XElement("Alias", property.Alias));

                var def = _dataTypeService.GetDataTypeDefinitionById(property.DataTypeDefinitionId);
                if (def != null)
                    propNode.Add(new XElement("Definition", def.Key));

                propNode.Add(new XElement("Type", property.PropertyEditorAlias));
                propNode.Add(new XElement("Mandatory", property.Mandatory));

                propNode.Add(new XElement("Validation", property.ValidationRegExp != null ? property.ValidationRegExp : "" ));

                var description = String.IsNullOrEmpty(property.Description) ? "" : property.Description;
                propNode.Add(new XElement("Description", new XCData(description)));

                propNode.Add(new XElement("SortOrder", property.SortOrder));

                var tab = item.PropertyGroups.FirstOrDefault(x => x.PropertyTypes.Contains(property));
                propNode.Add(new XElement("Tab", tab != null ? tab.Name : ""));

                if (_itemType == "MemberType")
                {
                    var canEdit = ((IMemberType)item).MemberCanEditProperty(property.Name);
                    var canView = ((IMemberType)item).MemberCanViewProperty(property.Name);

                    propNode.Add(new XElement("CanEdit", canEdit));
                    propNode.Add(new XElement("CanView", canView));
                }

                properties.Add(propNode);
            }

            return properties;
        }

    
        // special case for two pass, you can tell it to only first step
        public SyncAttempt<T> Deserialize(XElement node, bool forceUpdate, bool onePass = false)
        {
            var attempt = base.DeSerialize(node);

            if (!onePass || !attempt.Success || attempt.Item == null)
                return attempt;

            return DesearlizeSecondPass(attempt.Item, node);
        }

        virtual public SyncAttempt<T> DesearlizeSecondPass(T item, XElement node)
        {
            return SyncAttempt<T>.Succeed(node.NameFromNode(), ChangeType.NoChange);
        }

        #endregion

        #region Lookup Helpers
        /// <summary>
        ///  these shoud be doable with the entity service, but for now, we 
        ///  are grouping these making it eaiser should we add another 
        /// contentTypeBased type to it. 
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private IContentTypeBase LookupByKey(Guid key)
        {
            IContentTypeBase item = default(IContentTypeBase);
            switch (_itemType)
            {
                case Constants.Packaging.DocumentTypeNodeName:
                    item = _contentTypeService.GetContentType(key);
                    break;
                case "MediaType":
                    item = _contentTypeService.GetMediaType(key);
                    break;
                case "MemberType":
                    item = _memberTypeService.Get(key);
                    break;
            }

            return item;
        }

        private IContentTypeBase LookupById(int id)
        {
            IContentTypeBase item = default(IContentTypeBase);
            switch (_itemType)
            {
                case Constants.Packaging.DocumentTypeNodeName:
                    item = _contentTypeService.GetContentType(id);
                    break;
                case "MediaType":
                    item = _contentTypeService.GetMediaType(id);
                    break;
                case "MemberType":
                    item = _memberTypeService.Get(id);
                    break;
            }

            return item;
        }
        private IContentTypeBase LookupByAlias(string alias)
        {
            IContentTypeBase item = default(IContentTypeBase);
            switch (_itemType)
            {
                case Constants.Packaging.DocumentTypeNodeName:
                    item = _contentTypeService.GetContentType(alias);
                    break;
                case "MediaType":
                    item = _contentTypeService.GetMediaType(alias);
                    break;
                case "MemberType":
                    item = _memberTypeService.Get(alias);
                    break;
            }

            return item;
        }
        #endregion

    }
}
