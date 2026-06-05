@{
	permission    = "deny"
	user_message  = "このリポジトリでは git config --global は使えません。"
	agent_message = "グローバル Git 設定の変更は禁止です。必要なら git config --local を使ってください。"
} | ConvertTo-Json -Compress
exit 2
