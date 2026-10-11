# expect: none
$r = Invoke-RestMethod https://api.example.com/rates
$r.usd | Format-Table
