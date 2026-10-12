namespace ARMO.ViewModels

open CommunityToolkit.Mvvm.ComponentModel

[<AbstractClass>]
type ViewModelBase() =
    inherit ObservableObject()

    member this.Notify(name: string) = this.OnPropertyChanged(name)
