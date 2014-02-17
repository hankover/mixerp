﻿/********************************************************************************
Copyright (C) Binod Nepal, Mix Open Foundation (http://mixof.org).

This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. 
If a copy of the MPL was not distributed  with this file, You can obtain one at 
http://mozilla.org/MPL/2.0/.
***********************************************************************************/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using MixERP.Net.Common;
using Npgsql;
using MixERP.Net.DBFactory;
using MixERP.Net.Common.Models.Transactions;

namespace MixERP.Net.DatabaseLayer.Transactions
{
    public static class NonGLStockTransaction
    {
        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static long Add(string book, DateTime valueDate, int officeId, int userId, long logOnId, string referenceNumber, string statementReference, StockMasterModel stockMaster, Collection<StockMasterDetailModel> details, Collection<int> transactionIdCollection)
        {
            if (stockMaster == null)
            {
                return 0;
            }

            if (details == null)
            {
                return 0;
            }

            if (details.Count.Equals(0))
            {
                return 0;
            }

            string sql = string.Empty;
            long nonGlStockMasterId = 0;

            //decimal total = details.Sum(d => (d.Price * d.Quantity));
            //decimal discountTotal = details.Sum(d => d.Discount);
            //decimal taxTotal = details.Sum(d => d.Tax);

            using (NpgsqlConnection connection = new NpgsqlConnection(DBConnection.ConnectionString()))
            {
                connection.Open();

                using (NpgsqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {

                        #region NonGLStockMaster
                        sql = "INSERT INTO transactions.non_gl_stock_master(non_gl_stock_master_id, value_date, book, party_id, price_type_id, login_id, user_id, office_id, reference_number, statement_reference) SELECT nextval(pg_get_serial_sequence('transactions.non_gl_stock_master', 'non_gl_stock_master_id')), @ValueDate, @Book, core.get_party_id_by_party_code(@PartyCode), @PriceTypeId, @LoginId, @UserId, @OfficeId, @ReferenceNumber, @StatementReference; SELECT currval(pg_get_serial_sequence('transactions.non_gl_stock_master', 'non_gl_stock_master_id'));";

                        using (NpgsqlCommand stockMasterRow = new NpgsqlCommand(sql, connection))
                        {
                            stockMasterRow.Parameters.Add("@ValueDate", valueDate);
                            stockMasterRow.Parameters.Add("@Book", book);
                            stockMasterRow.Parameters.Add("@PartyCode", stockMaster.PartyCode);

                            if (stockMaster.PriceTypeId.Equals(0))
                            {
                                stockMasterRow.Parameters.Add("@PriceTypeId", DBNull.Value);
                            }
                            else
                            {
                                stockMasterRow.Parameters.Add("@PriceTypeId", stockMaster.PriceTypeId);
                            }

                            stockMasterRow.Parameters.Add("@LoginId", logOnId);
                            stockMasterRow.Parameters.Add("@UserId", userId);
                            stockMasterRow.Parameters.Add("@OfficeId", officeId);
                            stockMasterRow.Parameters.Add("@ReferenceNumber", referenceNumber);

                            stockMasterRow.Parameters.Add("@StatementReference", statementReference);

                            nonGlStockMasterId = Conversion.TryCastLong(stockMasterRow.ExecuteScalar());
                        }

                        #region NonGLStockDetails
                        sql = @"INSERT INTO 
                                transactions.non_gl_stock_details(non_gl_stock_master_id, item_id, quantity, unit_id, base_quantity, base_unit_id, price, discount, tax_rate, tax) 
                                SELECT  @NonGlStockMasterId, core.get_item_id_by_item_code(@ItemCode), @Quantity, core.get_unit_id_by_unit_name(@UnitName), core.get_base_quantity_by_unit_name(@UnitName, @Quantity), core.get_base_unit_id_by_unit_name(@UnitName), @Price, @Discount, @TaxRate, @Tax;";

                        foreach (StockMasterDetailModel model in details)
                        {
                            using (NpgsqlCommand stockMasterDetailRow = new NpgsqlCommand(sql, connection))
                            {
                                stockMasterDetailRow.Parameters.Add("@NonGlStockMasterId", nonGlStockMasterId);
                                stockMasterDetailRow.Parameters.Add("@ItemCode", model.ItemCode);
                                stockMasterDetailRow.Parameters.Add("@Quantity", model.Quantity);
                                stockMasterDetailRow.Parameters.Add("@UnitName", model.UnitName);
                                stockMasterDetailRow.Parameters.Add("@Price", model.Price);
                                stockMasterDetailRow.Parameters.Add("@Discount", model.Discount);
                                stockMasterDetailRow.Parameters.Add("@TaxRate", model.TaxRate);
                                stockMasterDetailRow.Parameters.Add("@Tax", model.Tax);

                                stockMasterDetailRow.ExecuteNonQuery();
                            }
                        }

                        #endregion

                        if (transactionIdCollection != null)
                        {
                            if (transactionIdCollection.Count > 0)
                            {
                                foreach (int tranId in transactionIdCollection)
                                {
                                    sql = "INSERT INTO transactions.non_gl_stock_master_relations(order_non_gl_stock_master_id, quotation_non_gl_stock_master_id) SELECT @Id, @RelationId;";
                                    using (NpgsqlCommand relation = new NpgsqlCommand(sql, connection))
                                    {
                                        relation.Parameters.Add("@Id", nonGlStockMasterId);
                                        relation.Parameters.Add("@RelationId", tranId);
                                        relation.ExecuteNonQuery();
                                    }                                
                                }
                            }
                        }

                        #endregion

                        transaction.Commit();
                        return nonGlStockMasterId;
                    }
                    catch (NpgsqlException)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public static DataTable GetView(int userId, string book, int officeId, DateTime dateFrom, DateTime dateTo, string office, string party, string priceType, string user, string referenceNumber, string statementReference)
        {
            string sql = "SELECT * FROM transactions.get_product_view(@UserId, @Book, @OfficeId, @DateFrom, @DateTo, @Office, @Party, @PriceType, @User, @ReferenceNumber, @StatementReference);";

            using (NpgsqlCommand command = new NpgsqlCommand(sql))
            {
                command.Parameters.Add("@UserId", userId);
                command.Parameters.Add("@Book", book);
                command.Parameters.Add("@OfficeId", officeId);
                command.Parameters.Add("@DateFrom", dateFrom);
                command.Parameters.Add("@DateTo", dateTo);
                command.Parameters.Add("@Office", office);
                command.Parameters.Add("@Party", party);
                command.Parameters.Add("@PriceType", priceType);
                command.Parameters.Add("@User", user);
                command.Parameters.Add("@ReferenceNumber", referenceNumber);
                command.Parameters.Add("@StatementReference", statementReference);

                return DBOperations.GetDataTable(command);
            }
        }

        public static bool TransactionIdsBelongToSameParty(Collection<int> ids)
        {
            if (ids == null)
            {
                return false;
            }

            //Crate an array object to store the parameters.
            var parameters = new string[ids.Count];

            //Iterate through the ids and create a parameter array.
            for (int i = 0; i < ids.Count; i++)
            {
                parameters[i] = "@Id" + Conversion.TryCastString(i);
            }

            //Get the count of distinct number of parties associated with the transaction id.
            string sql = "SELECT COUNT(DISTINCT party_id) FROM transactions.non_gl_stock_master WHERE non_gl_stock_master_id IN(" + string.Join(",", parameters) + ");";

            //Create a PostgreSQL command object from the SQL string.
            using (NpgsqlCommand command = new NpgsqlCommand(sql))
            {
                //Iterate through the IDs and add PostgreSQL parameters.
                for (int i = 0; i < ids.Count; i++)
                {
                    command.Parameters.Add("@Id" + Conversion.TryCastString(i), ids[i]);
                }

                //If the transactions associated with the supplied transaction ids belong to a single part,
                //the value should equal to 1.
                return Conversion.TryCastInteger(DBOperations.GetScalarValue(command)).Equals(1);
            }
        }

        public static bool AreSalesQuotationsAlreadyMerged(Collection<int> ids)
        {
            if (ids == null)
            {
                return false;
            }

            //Crate an array object to store the parameters.
            var parameters = new string[ids.Count];

            //Iterate through the ids and create a parameter array.
            for (int i = 0; i < ids.Count; i++)
            {
                parameters[i] = "@Id" + Conversion.TryCastString(i);
            }

            string sql = @"SELECT transactions.are_sales_quotations_already_merged(" + string.Join(",", parameters) + ");";

            using (NpgsqlCommand command = new NpgsqlCommand(sql))
            {
                //Iterate through the IDs and add PostgreSQL parameters.
                for (int i = 0; i < ids.Count; i++)
                {
                    command.Parameters.Add("@Id" + Conversion.TryCastString(i), ids[i]);
                }

                return Conversion.TryCastBoolean(DBOperations.GetScalarValue(command));
            }
        }

        public static bool AreSalesOrdersAlreadyMerged(Collection<int> ids)
        {
            if (ids == null)
            {
                return false;
            }

            //Crate an array object to store the parameters.
            var parameters = new string[ids.Count];

            //Iterate through the ids and create a parameter array.
            for (int i = 0; i < ids.Count; i++)
            {
                parameters[i] = "@Id" + Conversion.TryCastString(i);
            }

            string sql = @"SELECT transactions.are_sales_orders_already_merged(" + string.Join(",", parameters) + ");";

            using (NpgsqlCommand command = new NpgsqlCommand(sql))
            {
                //Iterate through the IDs and add PostgreSQL parameters.
                for (int i = 0; i < ids.Count; i++)
                {
                    command.Parameters.Add("@Id" + Conversion.TryCastString(i), ids[i]);
                }

                return Conversion.TryCastBoolean(DBOperations.GetScalarValue(command));
            }
        }
    }
}
