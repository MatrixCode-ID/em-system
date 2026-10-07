use winsafe::{co, gui, prelude::*};

/// The list of trusted public keys in the setup form and the maintenance window: the `keyId` and where
/// the key comes from, one row per key. Its content is replaced entirely with [`Self::fill`]; rows are
/// identified by their order.
#[derive(Clone)]
pub struct KeyListView {
   view: gui::ListView,
}

impl KeyListView {
   // region: Statics

   /// Creates the list in `parent` at `position` with size `size` (96 DPI pixels).
   pub fn new(parent: &(impl GuiParent + 'static), position: (i32, i32), size: (i32, i32)) -> Self {
      let key_width = 150;
      let view = gui::ListView::new(
         parent,
         gui::ListViewOpts {
            position: gui::dpi(position.0, position.1),
            size: gui::dpi(size.0, size.1),
            columns: &[
               ("Key ID", gui::dpi_x(key_width)),
               // Fills the rest of the list, without room for a vertical scroll bar.
               ("From", gui::dpi_x(size.0 - key_width - 24)),
            ],
            // One selection at a time: every action on the list works on one key.
            control_style: co::LVS::REPORT | co::LVS::NOSORTHEADER | co::LVS::SHOWSELALWAYS | co::LVS::SINGLESEL,
            ..Default::default()
         },
      );
      Self { view }
   }

   // endregion

   // region: Properties

   /// The ListView control itself, to enable/hide it or attach an event.
   pub fn view(&self) -> &gui::ListView {
      &self.view
   }

   /// The order of the selected row, or `None`.
   pub fn selected_index(&self) -> Option<usize> {
      self
         .view
         .items()
         .iter_selected()
         .next()
         .map(|item| item.index() as usize)
   }

   // endregion

   // region: Methods

   /// Replaces the content of the list with `rows` (`keyId`, where the key comes from).
   pub fn fill(&self, rows: &[(String, String)]) {
      let items = self.view.items();
      let _ = items.delete_all();
      for (key_id, origin) in rows {
         let _ = items.add(&[key_id.as_str(), origin.as_str()], None, ());
      }
   }

   // endregion
}
