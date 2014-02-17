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
using MixERP.Net.Common.Models.Transactions;
using MixERP.Net.DBFactory;
using Npgsql;

namespace MixERP.Net.DatabaseLayer.Transactions
{
    public static class SalesDelivery
    {
        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static long Add(DateTime valueDate, int officeId, int userId, long logOnId, int costCenterId, string referenceNumber, string statementReference, StockMasterModel stockMaster, Collection<StockMasterDetailModel> details, Collection<int> transactionIdCollection)
        {
            if(stockMaster == null)
            {
                return 0;
            }

            if(details == null)
            {
                return 0;
            }

            if(details.Count.Equals(0))
            {
                return 0;
            }

            if (stockMaster.AgentId.Equals(0))
            {
                return 0;
            }

            string sql = string.Empty;
            long transactionMasterId = 0;
            long stockMasterId = 0;

            decimal total = details.Sum(d => (d.Price * d.Quantity));
            decimal discountTotal = details.Sum(d => d.Discount);
            decimal taxTotal = details.Sum(d => d.Tax);

            string creditInvariantParameter = "Sales.Receivables";
            string salesInvariantParameter = "Sales";
            string salesTaxInvariantParamter = "Sales.Tax";
            string salesDiscountInvariantParameter = "Sales.Discount";


            using(NpgsqlConnection connection = new NpgsqlConnection(DBConnection.ConnectionString()))
            {
                connection.Open();

                using(NpgsqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {

                        #region TransactionMaster
                        sql = "INSERT INTO transactions.transaction_master(transaction_master_id, transaction_counter, transaction_code, book, value_date, user_id, login_id, office_id, cost_center_id, reference_number, statement_reference) SELECT nextval(pg_get_serial_sequence('transactions.transaction_master', 'transaction_master_id')), transactions.get_new_transaction_counter(@ValueDate), transactions.get_transaction_code(@ValueDate, @OfficeId, @UserId, @LogOnId), @Book, @ValueDate, @UserId, @LogOnId, @OfficeId, @CostCenterId, @ReferenceNumber, @StatementReference;SELECT currval(pg_get_serial_sequence('transactions.transaction_master', 'transaction_master_id'));";
                        using(NpgsqlCommand tm = new NpgsqlCommand(sql, connection))
                        {
                            tm.Parameters.Add("@ValueDate", valueDate);
                            tm.Parameters.Add("@OfficeId", officeId);
                            tm.Parameters.Add("@UserId", userId);
                            tm.Parameters.Add("@LogOnId", logOnId);
                            tm.Parameters.Add("@Book", "Sales.Delivery");
                            tm.Parameters.Add("@CostCenterId", costCenterId);
                            tm.Parameters.Add("@ReferenceNumber", referenceNumber);
                            tm.Parameters.Add("@StatementReference", statementReference);

                            transactionMasterId = Conversion.TryCastLong(tm.ExecuteScalar());
                        }

                        #region TransactionDetails
                        sql = "INSERT INTO transactions.transaction_details(transaction_master_id, tran_type, account_id, statement_reference, amount) SELECT @TransactionMasterId, @TranType, core.get_account_id_by_parameter(@ParameterName), @StatementReference, @Amount;";

                        using(NpgsqlCommand salesRow = new NpgsqlCommand(sql, connection))
                        {
                            salesRow.Parameters.Add("@TransactionMasterId", transactionMasterId);
                            salesRow.Parameters.Add("@TranType", "Cr");
                            salesRow.Parameters.Add("@ParameterName", salesInvariantParameter);
                            salesRow.Parameters.Add("@StatementReference", statementReference);
                            salesRow.Parameters.Add("@Amount", total);

                            salesRow.ExecuteNonQuery();
                        }

                        if(taxTotal > 0)
                        {
                            using(NpgsqlCommand taxRow = new NpgsqlCommand(sql, connection))
                            {
                                taxRow.Parameters.Add("@TransactionMasterId", transactionMasterId);
                                taxRow.Parameters.Add("@TranType", "Cr");
                                taxRow.Parameters.Add("@ParameterName", salesTaxInvariantParamter);
                                taxRow.Parameters.Add("@StatementReference", statementReference);
                                taxRow.Parameters.Add("@Amount", taxTotal);
                                taxRow.ExecuteNonQuery();
                            }
                        }

                        if(discountTotal > 0)
                        {
                            using(NpgsqlCommand discountRow = new NpgsqlCommand(sql, connection))
                            {
                                discountRow.Parameters.Add("@TransactionMasterId", transactionMasterId);
                                discountRow.Parameters.Add("@TranType", "Dr");
                                discountRow.Parameters.Add("@ParameterName", salesDiscountInvariantParameter);
                                discountRow.Parameters.Add("@StatementReference", statementReference);
                                discountRow.Parameters.Add("@Amount", discountTotal);
                                discountRow.ExecuteNonQuery();
                            }
                        }

                        using(NpgsqlCommand creditRow = new NpgsqlCommand(sql, connection))
                        {
                            creditRow.Parameters.Add("@TransactionMasterId", transactionMasterId);
                            creditRow.Parameters.Add("@TranType", "Dr");
                            creditRow.Parameters.Add("@ParameterName", creditInvariantParameter);
                            creditRow.Parameters.Add("@StatementReference", statementReference);
                            creditRow.Parameters.Add("@Amount", total - discountTotal + taxTotal + stockMaster.ShippingCharge);
                            creditRow.ExecuteNonQuery();
                        }

                        if(stockMaster.ShippingCharge > 0)
                        {
                            sql = "INSERT INTO transactions.transaction_details(transaction_master_id, tran_type, account_id, statement_reference, amount) SELECT @TransactionMasterId, @TranType, core.get_account_id_by_shipper_id(@ShipperId), @StatementReference, @Amount;";

                            using(NpgsqlCommand shippingChargeRow = new NpgsqlCommand(sql, connection))
                            {
                                shippingChargeRow.Parameters.Add("@TransactionMasterId", transactionMasterId);
                                shippingChargeRow.Parameters.Add("@TranType", "Cr");
                                shippingChargeRow.Parameters.Add("@ShipperId", stockMaster.ShipperId);
                                shippingChargeRow.Parameters.Add("@StatementReference", statementReference);
                                shippingChargeRow.Parameters.Add("@Amount", stockMaster.ShippingCharge);

                                shippingChargeRow.ExecuteNonQuery();
                            }
                        }
                        #endregion
                        #endregion

                        #region StockMaster

                        sql = "INSERT INTO transactions.stock_master(stock_master_id, transaction_master_id, party_id, agent_id, price_type_id, is_credit, shipper_id, shipping_charge, store_id) SELECT nextval(pg_get_serial_sequence('transactions.stock_master', 'stock_master_id')), @TransactionMasterId, core.get_party_id_by_party_code(@PartyCode), @AgentId, @PriceTypeId, @IsCredit, @ShipperId, @ShippingCharge, @StoreId; SELECT currval(pg_get_serial_sequence('transactions.stock_master', 'stock_master_id'));";

                        using(NpgsqlCommand stockMasterRow = new NpgsqlCommand(sql, connection))
                        {
                            stockMasterRow.Parameters.Add("@TransactionMasterId", transactionMasterId);
                            stockMasterRow.Parameters.Add("@PartyCode", stockMaster.PartyCode);
                            stockMasterRow.Parameters.Add("@AgentId", stockMaster.AgentId);
                            stockMasterRow.Parameters.Add("@PriceTypeId", stockMaster.PriceTypeId);
                            stockMasterRow.Parameters.Add("@IsCredit", true);

                            if(stockMaster.ShipperId.Equals(0))
                            {
                                stockMasterRow.Parameters.Add("@ShipperId", DBNull.Value);
                            }
                            else
                            {
                                stockMasterRow.Parameters.Add("@ShipperId", stockMaster.ShipperId);
                            }

                            stockMasterRow.Parameters.Add("@ShippingCharge", stockMaster.ShippingCharge);

                            stockMasterRow.Parameters.Add("@StoreId", stockMaster.StoreId);

                            stockMasterId = Conversion.TryCastLong(stockMasterRow.ExecuteScalar());
                        }

                        #region StockDetails
                        sql = @"INSERT INTO 
                                transactions.stock_details(stock_master_id, tran_type, store_id, item_id, quantity, unit_id, base_quantity, base_unit_id, price, discount, tax_rate, tax) 
                                SELECT  @StockMasterId, @TranType, @StoreId, core.get_item_id_by_item_code(@ItemCode), @Quantity, core.get_unit_id_by_unit_name(@UnitName), core.get_base_quantity_by_unit_name(@UnitName, @Quantity), core.get_base_unit_id_by_unit_name(@UnitName), @Price, @Discount, @TaxRate, @Tax;";

                        foreach(StockMasterDetailModel model in details)
                        {
                            using(NpgsqlCommand stockMasterDetailRow = new NpgsqlCommand(sql, connection))
                            {
                                stockMasterDetailRow.Parameters.Add("@StockMasterId", stockMasterId);
                                stockMasterDetailRow.Parameters.Add("@TranType", "Cr");
                                stockMasterDetailRow.Parameters.Add("@StoreId", model.StoreId);
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

                        #endregion

                        if (transactionIdCollection != null)
                        {
                            if (transactionIdCollection.Count > 0)
                            {
                                foreach (int tranId in transactionIdCollection)
                                {
                                    sql = "INSERT INTO transactions.stock_master_non_gl_relations(stock_master_id, non_gl_stock_master_id) SELECT @Id, @RelationId;";
                                    using (NpgsqlCommand relation = new NpgsqlCommand(sql, connection))
                                    {
                                        relation.Parameters.Add("@Id", transactionMasterId);
                                        relation.Parameters.Add("@RelationId", tranId);
                                        relation.ExecuteNonQuery();
                                    }
                                }
                            }
                        }

                        transaction.Commit();
                        return transactionMasterId;
                    }
                    catch(NpgsqlException)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

    }
}