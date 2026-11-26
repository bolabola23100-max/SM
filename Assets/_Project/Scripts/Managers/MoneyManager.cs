using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Central money ledger for the supermarket economy.</summary>
public class MoneyManager : MonoBehaviour
{
    public static MoneyManager Instance { get; private set; }
    [Serializable] public class Transaction { public string label; public float amount; public string category; public string timestamp; }
    [Serializable] private class SaveState { public float balance; public float totalRevenue; public float totalExpenses; public float discountsGiven; public List<Transaction> transactions = new List<Transaction>(); }

    [Header("Starting Money")]
    [Min(0)] public float startingBalance = 1000f;
    [Header("Discounts")]
    [Range(0,100)] public float supplierDiscountPercent;
    [Range(0,100)] public float customerDiscountPercent;
    [Header("History")]
    [Min(10)] public int maxHistory = 100;

    public float Balance { get; private set; }
    public float TotalRevenue { get; private set; }
    public float TotalExpenses { get; private set; }
    public float DiscountsGiven { get; private set; }
    public float NetProfit => TotalRevenue - TotalExpenses;
    public IReadOnlyList<Transaction> History => transactions;
    public event Action Changed;
    private List<Transaction> transactions = new List<Transaction>();
    private const string SaveKey = "SM_MoneyManager_Save_v1";

    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance=this; Load(); }
    public float ApplySupplierDiscount(float total) => Mathf.Max(0,total)*(1f-supplierDiscountPercent/100f);
    public float ApplyCustomerDiscount(float total) => Mathf.Max(0,total)*(1f-customerDiscountPercent/100f);

    public bool TrySpend(float amount,string reason,string category="Expense")
    {
        amount=Mathf.Max(0,amount); if(Balance+0.0001f<amount)return false;
        Balance-=amount; TotalExpenses+=amount; Record(reason,-amount,category); Save(); return true;
    }
    public void AddRevenue(float amount,string reason,string category="Sale")
    {
        amount=Mathf.Max(0,amount); Balance+=amount; TotalRevenue+=amount; Record(reason,amount,category); Save();
    }
    public void AddExpense(float amount,string reason,string category="Expense")
    {
        amount=Mathf.Max(0,amount); Balance-=amount; TotalExpenses+=amount; Record(reason,-amount,category); Save();
    }
    public void GiveDiscount(float originalAmount,float finalAmount,string reason="Customer discount")
    {
        float discount=Mathf.Max(0,originalAmount-finalAmount); DiscountsGiven+=discount; Record(reason,-discount,"Discount"); Save();
    }
    public bool TryPurchaseStock(float total,string reason,out float discountedTotal)
    {
        discountedTotal=ApplySupplierDiscount(total);
        if(!TrySpend(discountedTotal,reason,"Stock Purchase"))return false;
        if(total>discountedTotal) Record("Supplier discount saved",total-discountedTotal,"Discount Received");
        Save(); return true;
    }
    private void Record(string label,float amount,string category)
    {
        transactions.Insert(0,new Transaction{label=label,amount=amount,category=category,timestamp=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")});
        if(transactions.Count>maxHistory)transactions.RemoveRange(maxHistory,transactions.Count-maxHistory);
        Changed?.Invoke();
    }
    private void Save()
    {
        SaveState s=new SaveState{balance=Balance,totalRevenue=TotalRevenue,totalExpenses=TotalExpenses,discountsGiven=DiscountsGiven,transactions=transactions};
        PlayerPrefs.SetString(SaveKey,JsonUtility.ToJson(s)); PlayerPrefs.Save();
    }
    private void Load()
    {
        // Testing mode: always start with the configured balance (1000 by default).
        Balance=startingBalance;
        if(!PlayerPrefs.HasKey(SaveKey)){Save();return;}
        SaveState s=JsonUtility.FromJson<SaveState>(PlayerPrefs.GetString(SaveKey));
        if(s==null){Save();return;}
        // Keep history and stats, but never restore the previous balance.
        TotalRevenue=s.totalRevenue;TotalExpenses=s.totalExpenses;DiscountsGiven=s.discountsGiven;transactions=s.transactions??new List<Transaction>();
        Save();
    }
    public void ResetEconomy(){Balance=startingBalance;TotalRevenue=0;TotalExpenses=0;DiscountsGiven=0;transactions.Clear();Save();Changed?.Invoke();}
}