import 'package:impinj_r700_mobile/models/antenna_port_state.dart';
import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:impinj_r700_mobile/models/reader_info_model.dart';
import 'package:impinj_r700_mobile/models/tag_read_event.dart';

abstract class ReaderService {
  Future<ReaderInfoModel> connect(ReaderConnectionConfig config);

  Future<void> disconnect();

  Future<List<AntennaPortState>> fetchAntennas();

  Future<void> updateEnabledAntennas(List<int> ports);

  Future<void> startReading({required List<int> ports});

  Future<void> stopReading();

  Stream<TagReadEvent> openTagEventStream();
}
