class ReaderInfoModel {
  const ReaderInfoModel({
    required this.productModel,
    required this.productDescription,
    required this.serialNumber,
    required this.firmwareVersion,
    required this.antennaCount,
    required this.interfaceName,
    required this.readerStatus,
  });

  final String productModel;
  final String productDescription;
  final String serialNumber;
  final String firmwareVersion;
  final int antennaCount;
  final String interfaceName;
  final String readerStatus;
}
