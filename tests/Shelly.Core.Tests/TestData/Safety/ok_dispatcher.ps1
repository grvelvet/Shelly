# expect: none
$form.Invoke([Action]{ $label.Text = 'x' })
$window.Dispatcher.Invoke({ $x = 1 })
