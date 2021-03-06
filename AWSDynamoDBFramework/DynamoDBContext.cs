﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Expression = Amazon.DynamoDBv2.DocumentModel.Expression;
using System.Threading;

namespace AWSDynamoDBFramework
{
    public class DynamoDBContext
    {
        private AmazonDynamoDBClient client;
        private Table dynamoDBTable;
        public AWSDynamoTableConfig AWSDynamoTableConfig { get; set; }
        public List<AWSDocumentConverter> StartupData { get; private set; } // Not implemented proper. Changed
        public AWSDynamoDBTable AWSDynamoDBTable { get; set; }
        private BasicAWSCredentials BasicAWSCredentials { get; set; }
        private RegionEndpoint RegionEndpoint { get; set; }
        private AWSDocumentConverter AWSDocumentConverter { get; set; }

        public DynamoDBContext(RegionEndpoint regionEndpoint, string accessKey, string secretKey, AWSDynamoTableConfig tableConfig, List<AWSDocumentConverter> startupData = null)
        {
            this.BasicAWSCredentials = new BasicAWSCredentials(accessKey, secretKey);
            this.RegionEndpoint = regionEndpoint;
            this.client = new AmazonDynamoDBClient( new BasicAWSCredentials(accessKey, secretKey), new AmazonDynamoDBConfig() { RegionEndpoint = regionEndpoint });
            this.AWSDynamoTableConfig = tableConfig;
            this.StartupData = startupData;
            this.AWSDocumentConverter = new AWSDocumentConverter();

            initiateTable();
        }

        public DynamoDBContext(BasicAWSCredentials credentials, RegionEndpoint regionEndpoint, AWSDynamoTableConfig tableConfig, List<AWSDocumentConverter> startupData = null)
        {
            this.BasicAWSCredentials = credentials;
            this.RegionEndpoint = regionEndpoint;
            this.client = new AmazonDynamoDBClient(credentials, new AmazonDynamoDBConfig() { RegionEndpoint = regionEndpoint });
            this.AWSDynamoTableConfig = tableConfig;
            this.StartupData = startupData;
            this.AWSDocumentConverter = new AWSDocumentConverter();

            initiateTable();
        }

        private void initiateTable()
        {
            // Check if Table exists
            AWSDynamoDBTable = new AWSDynamoDBTable(client, AWSDynamoTableConfig.TableName, AWSDynamoTableConfig.KeyName, AWSDynamoTableConfig.KeyType, AWSDynamoTableConfig.ReadCapacityUnits, AWSDynamoTableConfig.WriteCapacityUnits, AWSDynamoTableConfig.StreamEnabled);
            
            // Create table if not
            var createNewDynamoDB = !AWSDynamoDBTable.TableExists();
            if (createNewDynamoDB)
                AWSDynamoDBTable.ExecuteCreateTable(true); // Wait for table to be created.

            // Load table
            dynamoDBTable = Table.LoadTable(client, AWSDynamoTableConfig.TableName);

            // Load startup data
            if (createNewDynamoDB && StartupData != null && StartupData.Count > 0)
                BatchInsert(StartupData);
        }


        public void Insert<T>(T tObject)
        {
            var document = AWSDocumentConverter.ToDocument(tObject);
            dynamoDBTable.PutItem(document);
        }

        public Document Get(string id, bool consistentRead = true, List<string> attributesToGet = null)
        {
            GetItemOperationConfig config = new GetItemOperationConfig();
            if (attributesToGet != null)
                config.AttributesToGet = attributesToGet;
            config.ConsistentRead = consistentRead;

            return dynamoDBTable.GetItem(id);
        }

        public T Get<T>(string id, bool consistentRead = true, List<string> attributesToGet = null)
        {
            var document = Get(id, consistentRead, attributesToGet);
            var result = (T)AWSDocumentConverter.ToObject<T>(document);
            return result;
        }

        public void DeleteDocument(string id)
        {
            dynamoDBTable.DeleteItem(id);
        }


        public Document PartialUpdateCommand<T>(T tObject, ReturnValues returnValues = ReturnValues.AllNewAttributes)
        {
            var document = AWSDocumentConverter.ToDocument(tObject);

            UpdateItemOperationConfig config = new UpdateItemOperationConfig
            {
                ReturnValues = returnValues
            };

            return dynamoDBTable.UpdateItem(document, config);
        }

        public void BatchInsert<T>(List<T> batchOfObjects)
        {
            var batchWrite = dynamoDBTable.CreateBatchWrite();

            foreach (var tObject in batchOfObjects)
            {
                var document = AWSDocumentConverter.ToDocument(tObject);
                batchWrite.AddDocumentToPut(document);
            }
            batchWrite.Execute();
        }

        public void BatchDelete(List<string> idList)
        {
            var batchWrite = dynamoDBTable.CreateBatchWrite();

            foreach (var id in idList)
            {
                batchWrite.AddKeyToDelete(new Primitive(id));
            }
            batchWrite.Execute();
        }

        public List<Document> BatchGet(List<string> idList, bool consistentRead = true, List<string> attributesToGet = null)
        {
            var batchGet = dynamoDBTable.CreateBatchGet();

            batchGet.ConsistentRead = consistentRead;
            if (attributesToGet != null)
                batchGet.AttributesToGet = attributesToGet;

            foreach (var id in idList)
            {
                batchGet.AddKey(new Primitive(id));
            }

            batchGet.Execute();

            return batchGet.Results;
        }

        public List<T> BatchGet<T>(List<string> idList, bool consistentRead = true, List<string> attributesToGet = null)
            where T : new()
        {
            List<T> tList = new List<T>();
            var documents = BatchGet(idList, consistentRead, attributesToGet);

            foreach (var document in documents)
            {
                T t = new T();
                t = (T)AWSDocumentConverter.ToObject<T>(document);
                tList.Add(t);
            }

            return tList;
        }

        public IEnumerable<Document> Scan(List<ScanFilterCondition> scanFilterConditions, bool consistentRead = true, List<string> attributesToGet = null )
        {
            ScanFilter scanFilter = new ScanFilter();

            foreach (var scanFilterCondition in scanFilterConditions)
            {
                if(scanFilterCondition.Type == ScanFilterConditionType.one)
                    scanFilter.AddCondition(scanFilterCondition.AttributeName, scanFilterCondition.Condition);
                if (scanFilterCondition.Type == ScanFilterConditionType.two)
                    scanFilter.AddCondition(scanFilterCondition.AttributeName, scanFilterCondition.ScanOperator, scanFilterCondition.AttributeValues);
                if (scanFilterCondition.Type == ScanFilterConditionType.three)
                    scanFilter.AddCondition(scanFilterCondition.AttributeName, scanFilterCondition.ScanOperator, scanFilterCondition.DynamoDBEntry);
            }
            
            ScanOperationConfig scanOperationConfig = new ScanOperationConfig()
            {
                ConsistentRead = consistentRead,
                Filter = scanFilter 
            };
            if (attributesToGet != null)
                scanOperationConfig.AttributesToGet = attributesToGet;

            Search search = dynamoDBTable.Scan(scanOperationConfig);
            
            List<Document> documentList = new List<Document>();
            do
            {
                documentList = search.GetNextSet();

                foreach (var document in documentList)
                    yield return document;
            } while (!search.IsDone);
        }

        public IEnumerable<T> Scan<T>(List<ScanFilterCondition> scanFilterConditions, bool consistentRead = true, List<string> attributesToGet = null)
        {
            foreach (var document in Scan(scanFilterConditions, consistentRead, attributesToGet))
            {
                var result = (T)AWSDocumentConverter.ToObject<T>(document);
                yield return result;
            }
        }

        public void AtomicCounter(string id, string attribute, int value)
        {
            var request = new UpdateItemRequest
            {
                TableName = AWSDynamoTableConfig.TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    { AWSDynamoTableConfig.KeyName, new AttributeValue { S = id } }
                },
                AttributeUpdates = new Dictionary<string, AttributeValueUpdate>()
                {
                    {
                        attribute,
                        new AttributeValueUpdate { Action = "ADD", Value = new AttributeValue { N = value.ToString() } }
                    },
                },
            };

            client.UpdateItem(request);
        }

        public void ConditionalUpdate<T>(T tObject, string condAttribute, object expectedValue)
        {
            var splittedAttributes = condAttribute.Split('.');

            var specificAttributeToCheck = condAttribute;
            string first = "";
            string last = condAttribute;
            if (splittedAttributes.Length > 1)
            {
                last = splittedAttributes.Last();
                first = condAttribute.Remove(condAttribute.Length - last.Length);  
            }

            Expression expr = new Expression();
            expr.ExpressionStatement = first+"#Cond = :Cond";
            expr.ExpressionAttributeNames["#Cond"] = last;
            if(expectedValue.GetType() == typeof(int))
                expr.ExpressionAttributeValues[":Cond"] = (int)expectedValue;
            if (expectedValue.GetType() == typeof(string))
                expr.ExpressionAttributeValues[":Cond"] = (string)expectedValue;

            UpdateItemOperationConfig config = new UpdateItemOperationConfig()
            {
                ConditionalExpression = expr,
                ReturnValues = ReturnValues.AllNewAttributes
            };

            dynamoDBTable.UpdateItem(AWSDocumentConverter.ToDocument(tObject), config);

        }

        public void Query()
        {
            /*
             * The Query method enables you to query your tables. 
             * You can only query the tables that have a composite primary key (partition key and sort key). 
             * If your dynamoDBTable's primary key is made of only a partition key, 
             * then the Query operation is not supported. By default, Query internally performs queries that are eventually consistent. 
             * To learn about the consistency model.
             */
            
            throw new NotImplementedException("Not implemented");
        }

        public AWSDynamoDBStream GetAWSDynamoDBStream(AWSDynamoDBIteratorType type)
        {
            return new AWSDynamoDBStream(this.BasicAWSCredentials, this.RegionEndpoint, this.AWSDynamoTableConfig.TableName, type);
        }
        
    }
}
