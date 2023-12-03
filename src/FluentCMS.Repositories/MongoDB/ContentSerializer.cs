//using FluentCMS.Entities;
//using MongoDB.Bson;
//using MongoDB.Bson.Serialization;
//using MongoDB.Bson.Serialization.Serializers;
//using System;
//using System.Collections.Generic;

//public class ContentSerializer : IBsonSerializer<Content>
//{
//    public Type ValueType => typeof(Content);

//    public Content Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
//    {
//        var document = BsonDocumentSerializer.Instance.Deserialize(context);

//        var content = new Content();
//        foreach (var element in document)
//        {
//            switch (element.Name)
//            {
//                case nameof(Content.Id):
//                    content.Id = element.Value.AsGuid;
//                    break;
//                case nameof(Content.CreatedBy):
//                    content.CreatedBy = element.Value.AsString;
//                    break;
//                case nameof(Content.CreatedAt):
//                    content.CreatedAt = element.Value.ToUniversalTime();
//                    break;
//                // Handle other Content properties
//                default:
//                    content[element.Name] = element.Value;
//                    break;
//            }
//        }
//        return content;
//    }

//    public void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Content value)
//    {
//        var document = new BsonDocument
//        {
//            { nameof(Content.Id), value.Id },
//            { nameof(Content.CreatedBy), value.CreatedBy },
//            { nameof(Content.CreatedAt), value.CreatedAt },
//            // Serialize other Content properties
//        };

//        foreach (var kvp in value)
//        {
//            document[kvp.Key] = BsonValue.Create(kvp.Value);
//        }

//        BsonDocumentSerializer.Instance.Serialize(context, document);
//    }

//    public void Serialize(BsonSerializationContext context, BsonSerializationArgs args, object value)
//    {
//        Serialize(context, args, (Content)value);
//    }

//    object IBsonSerializer.Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
//    {
//        return Deserialize(context, args);
//    }
//}
