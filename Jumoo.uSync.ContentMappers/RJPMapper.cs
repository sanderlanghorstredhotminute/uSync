﻿using Jumoo.uSync.Core.Mappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core;
using Umbraco.Core.Services;

namespace Jumoo.uSync.ContentMappers
{
    public class RJPMapper : IContentMapper
    {
        IEntityService _entityService;

        public RJPMapper()
        {
            _entityService = ApplicationContext.Current.Services.EntityService;
        }

        public string GetExportValue(int dataTypeDefinitionId, string value)
        {
            var links = JsonConvert.DeserializeObject<JArray>(value);
            if (links != null)
            {
                foreach(dynamic link in links)
                {
                    if (link.id != null)
                    {
                        var key = _entityService.Get((int)link.id);
                        if (key != null)
                        {
                            link.id = key.Key;
                        }
                    }
                }
            }

            return JsonConvert.SerializeObject(links, Formatting.Indented);
        }

        public string GetImportValue(int dataTypeDefinitionId, string content)
        {
            var links = JsonConvert.DeserializeObject<JArray>(content);
            if (links != null)
            {
                foreach (dynamic link in links)
                {
                    if (link.id != null)
                    {
                        Guid key;
                        if (Guid.TryParse(link.id.ToString(), out key))
                        {
                            var id = _entityService.GetByKey(key);
                            if (id != null)
                            {
                                link.id = id.Id;
                            }
                        }
                    }
                }
            }

            return JsonConvert.SerializeObject(links, Formatting.Indented);

        }
    }
}
