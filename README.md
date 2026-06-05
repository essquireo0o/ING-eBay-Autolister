# ING Listing Engine™
### by ING Mining LLC

**AI-powered eBay listing tool.** Paste a product URL, Claude AI fills in the title, description, category, price, and photos automatically. Bulk-import entire collections in one click.

---

## Download & Install

👉 **[Download Latest Release →](../../releases/latest)**

1. Download `AutoListerB1.exe`
2. Double-click it — no install required, runs instantly
3. Your browser opens at `http://localhost:9330`
4. Follow the setup steps below to enter your API keys

> Windows only. No .NET install needed — everything is bundled inside the exe.

---

## First-Time Setup

The app needs three things configured before it works:
1. An **Anthropic API key** (powers the AI)
2. An **eBay Developer account** with App credentials
3. An **eBay OAuth token** (links your seller account)

Click the **⚙ Settings** button in the top-right corner of the app to enter all of these.

---

## Step 1 — Get an Anthropic API Key

The AI that reads product pages and fills your listings runs on Claude.

1. Go to **[console.anthropic.com](https://console.anthropic.com)**
2. Sign up or log in
3. Click **API Keys** in the left sidebar
4. Click **Create Key** → copy the key (starts with `sk-ant-`)
5. Paste it into **Settings → Anthropic API Key** in the app

> You will be billed per use by Anthropic. A typical listing costs less than $0.01.

---

## Step 2 — Set Up an eBay Developer Account

### 2a. Create a developer account

1. Go to **[developer.ebay.com](https://developer.ebay.com)**
2. Click **Sign In** → use your regular eBay seller account to log in
3. Accept the developer agreement if prompted

### 2b. Create a Production Application

1. In the developer portal, go to **Application Keys**
2. Click **Create Application**
3. Give it any name (e.g. `AutoLister`)
4. Select **Production** (not Sandbox)
5. Click **Create**

You will now see three keys — copy all three:

| Key Name | Where to paste in the app |
|----------|--------------------------|
| **App ID** (Client ID) | Settings → eBay App ID |
| **Dev ID** | Settings → eBay Dev ID |
| **Cert ID** (Client Secret) | Settings → eBay Cert ID |

### 2c. Set Up Your OAuth Redirect (RuName)

The app needs an OAuth redirect URL so eBay can send your token back.

1. In the developer portal, go to **User Tokens**
2. Click **Get a Token from eBay via Your Application**
3. Under **Your auth accepted URL**, add:
   ```
   https://signin.ebay.com/ws/eBayISAPI.dll
   ```
4. Copy the **RuName** shown (looks like `YourName-AppName-PRD-xxxx`)
5. Paste it into **Settings → eBay RuName** in the app

### 2d. Connect Your eBay Seller Account

1. In the app, go to **Settings**
2. Fill in your App ID, Dev ID, Cert ID, and RuName
3. Click **Connect eBay Account**
4. A browser window opens — log in with your **eBay seller account**
5. Click **Agree** to grant access
6. The app stores your token automatically — you're connected

> The token lasts 18 months and auto-refreshes. You only need to do this once.

---

## Step 3 — Set Listing Defaults (Optional)

In **Settings → Listing Defaults**, you can pre-fill:
- Your postal code (for shipping estimates)
- Default handling time
- Default package weight/dimensions
- Fulfillment policy (shipping profile)

---

## How to Use

### Single Product
1. Click **New Listing**
2. Paste any product URL into the top bar
3. Click **Analyze** — AI fills everything in ~10 seconds
4. Review, edit if needed
5. Click **Publish to eBay**

### Bulk Import (Entire Collection)
1. Click **New Listing**
2. Paste a **category or collection page URL** (e.g. a supplier's product listing page) into the **Import All** bar
3. Click **Import All** — the AI processes every product on the page
4. Each product opens as a separate tab
5. Review and publish each one

### Managing Drafts
- Click **Save Draft** to save a listing locally before publishing
- Click **Open All Drafts** to reload previously saved drafts
- Click **Clear All** to wipe drafts and start fresh

---

## Troubleshooting

**The app doesn't open in my browser**
- Make sure nothing else is running on port 9330
- Try opening `http://localhost:9330` manually

**eBay token expired**
- Go to Settings → click **Connect eBay Account** again

**AI analysis fails**
- Check your Anthropic API key is correct in Settings
- Make sure you have credits on your Anthropic account

**"Address already in use" error**
- An old copy of the app is still running in the background
- Open Task Manager, find `AutoListerB1.exe`, and end the task

---

## Built With

- [Claude AI](https://anthropic.com) — product analysis and listing generation
- [eBay Sell API](https://developer.ebay.com) — listing creation and publishing
- ASP.NET Core 10 — backend server
- Vanilla JS — frontend UI

---

*ING Listing Engine™ is a product of ING Mining LLC. All rights reserved.*
