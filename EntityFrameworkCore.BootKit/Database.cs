﻿using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;

namespace EntityFrameworkCore.BootKit
{
    public class Database
    {
        internal List<DatabaseBind> DbContextBinds;

        public Database()
        {
            DbContextBinds = new List<DatabaseBind>();
        }

        internal DatabaseBind GetBinding(Type tableInterface)
        {
            var binding = DbContextBinds.FirstOrDefault(x => (x.TableInterface != null && x.TableInterface.Equals(tableInterface)) || (x.Entities != null && x.Entities.Contains(tableInterface)));

            if(binding == null)
            {
                throw new Exception($"Can't find binding for interface {tableInterface.ToString()}");
            }

            return binding;
        }

        internal DatabaseBind GetBinding(string tableName)
        {
            var binding = DbContextBinds.FirstOrDefault(x => x.Entities != null && x.Entities.Select(entity => entity.Name.ToLower()).Contains(tableName.ToLower()));

            if (binding == null)
            {
                throw new Exception($"Can't find binding for table {tableName}");
            }

            return binding;
        }

        private List<Type> GetAllEntityTypes(DatabaseBind bind)
        {
            var assemblies = (string[])AppDomain.CurrentDomain.GetData("Assemblies");
            return Utility.GetClassesWithInterface(bind.TableInterface, assemblies);
        }

        public void BindDbContext(DatabaseBind bind)
        {
            bind.Entities = GetAllEntityTypes(bind).ToList();

            DbContextBinds.Add(bind);
        }

        public void BindDbContext<TTableInterface, TDbContextType>(DatabaseBind bind)
        {
            bind.TableInterface = typeof(TTableInterface);
            bind.DbContextType = typeof(TDbContextType);

            bind.Entities = GetAllEntityTypes(bind).ToList();

            DbContextBinds.Add(bind);

            if (bind.CreateDbIfNotExist)
            {
                GetMaster(bind.TableInterface).Database.EnsureCreated();
            }
        }

        public DataContext GetMaster(Type tableInterface)
        {
            var binding = GetBinding(tableInterface);

            if (binding.DbContextMaster == null)
            {
                DbContextOptions options = new DbContextOptions<DataContext>();
                DataContext dbContext = Activator.CreateInstance(binding.DbContextType, options) as DataContext;
                dbContext.ConnectionString = binding.MasterConnection.ConnectionString;
                dbContext.EntityTypes = binding.Entities;
                binding.DbContextMaster = dbContext;
            }

            return binding.DbContextMaster;
        }

        public DataContext GetReader(Type tableInterface)
        {
            var binding = GetBinding(tableInterface);

            if (binding.DbContextSlavers == null)
            {
                binding.DbContextSlavers = new List<DataContext>();

                DbContextOptions options = new DbContextOptions<DataContext>();

                if (binding.SlaveConnections == null || binding.SlaveConnections.Count == 0)
                {
                    DataContext dbContext = Activator.CreateInstance(binding.DbContextType, options) as DataContext;

                    dbContext.ConnectionString = binding.MasterConnection.ConnectionString;
                    dbContext.EntityTypes = binding.Entities;
                    binding.DbContextSlavers.Add(dbContext);
                }
                else
                {
                    binding.SlaveConnections.ForEach(slaveConn =>
                    {
                        DataContext dbContext = Activator.CreateInstance(binding.DbContextType, options) as DataContext;
                        dbContext.ConnectionString = slaveConn.ConnectionString;
                        dbContext.EntityTypes = binding.Entities;
                        binding.DbContextSlavers.Add(dbContext);
                    });
                }
            }

            int slaver = new Random().Next(binding.DbContextSlavers.Count);

            return binding.DbContextSlavers[slaver];
        }

        private string RandomConn(List<DbConnection> connetions)
        {
            int idx = new Random().Next(connetions.Count);
            return connetions[idx].ConnectionString;
        }

        /*public IMongoCollection<T> Collection<T>(string collection) where T : class
        {
            DatabaseBind binding = GetBinding(typeof(T));
            if (binding.DbContextMaster == null)
            {
                binding.DbContext = new MongoDbContext(binding.ConnectionString);
            }

            return binding.DbContextSlavers.First();
        }*/

        public Object Find(Type type, params string[] keys)
        {
            DatabaseBind binding = DbContextBinds.First(x => x.Entities.Contains(type));
            if (binding.DbContextMaster == null || binding.DbContextMaster.Database.CurrentTransaction == null)
            {
                return GetReader(type).Find(type, keys);
            }
            else
            {
                return GetMaster(type).Find(type, keys);
            }
        }

        public void Add<TTableInterface>(Object entity)
        {
            var db = GetMaster(typeof(TTableInterface));
            db.Add(entity);
        }

        public void Add(Object entity)
        {
            var db = GetMaster(typeof(IDbRecord));
            db.Add(entity);
        }

        public DbSet<T> Table<T>() where T : class
        {
            Type entityType = typeof(T);

            DatabaseBind binding = DbContextBinds.First(x => x.Entities.Contains(entityType));
            if (binding.DbContextMaster == null || binding.DbContextMaster.Database.CurrentTransaction == null)
            {
                return GetReader(entityType).Set<T>();
            }
            else
            {
                return GetMaster(entityType).Set<T>();
            }
        }

        public IMongoCollection<T> Collection<T>(string name = "") where T : class
        {
            Type entityType = typeof(T);

            DatabaseBind binding = DbContextBinds.First(x => x.GetType().Equals(typeof(DatabaseBind))) as DatabaseBind;
            var db = GetMaster(entityType);
            return (db as DbContext4MongoDb).Set<T>(name);
        }

        public int ExecuteSqlCommand<T>(string sql, params object[] parameterms)
        {
            var db = GetMaster(typeof(T)).Database;
            return db.ExecuteSqlCommand(sql, parameterms);
        }

        public int ExecuteSqlCommand<T>(string sql, IEnumerable<object> parameterms)
        {
            var db = GetMaster(typeof(T)).Database;
            return db.ExecuteSqlCommand(sql, parameterms);
        }

        public int SaveChanges()
        {
            DatabaseBind binding = DbContextBinds.Where(x => x.DbContextType != null).First(x => x.DbContextMaster != null && x.DbContextMaster.Database.CurrentTransaction != null);
            return binding.DbContextMaster.SaveChanges();
        }
    }
}
